using System.IO;
using UnityEngine;
using static System.Math;

public class BlueprintBuilderMod : FortressCraftMod
{
    public static uint BuilderType = ModManager.mModMappings.CubesByKey["steveman0.BlueprintBuilder"].CubeType;
    public static int BlueprintMakerID = ModManager.mModMappings.ItemsByKey["steveman0.BlueprintMaker"].ItemId;
    public static BlueprintMakerWindow makerWindow = new BlueprintMakerWindow();
    public const string BLUEPRINT_FOLDER = "steveman0.Blueprints";
    public static string BlueprintPath;
    public bool Loaded = false;

    //Instancing
    public static Instancer[] HoloInstancers;

    //Blueprint zone highlight effect
    //static ParticleSystem BlueprintZone = GameObject.Find("ScanEffect").GetComponent<ParticleSystem>();
    ParticleSystem StartEffect;
    ParticleSystem EndEffect;
    GameObject BlueprintZone;
    public static bool ZoneDirty;
    public static bool ZoneActive = false;
    public static float ZoneTimer;
    

    public override ModRegistrationData Register()
    {
        ModRegistrationData modRegistrationData = new ModRegistrationData();

        modRegistrationData.RegisterEntityHandler("steveman0.BlueprintBuilder");
        modRegistrationData.RegisterEntityUI("steveman0.BlueprintBuilder", new BlueprintBuilderWindow());
        modRegistrationData.RegisterClientComms("steveman0.SendBlueprintToServer", Blueprint.SendToServer, Blueprint.ServerRead);
        modRegistrationData.RegisterClientComms("steveman0.RequestBlueprint", Blueprint.RequestBlueprint, Blueprint.AnswerBlueprintRequest);
        modRegistrationData.RegisterServerComms("steveman0.SendBlueprintToClient", Blueprint.SendToClient, Blueprint.ClientRead);
        modRegistrationData.RegisterClientComms("steveman0.RequestListBlueprint", Blueprint.RequestBlueprintList, Blueprint.AnswerListRequest);
        modRegistrationData.RegisterServerComms("steveman0.SendBlueprintList", Blueprint.SendBlueprintList, Blueprint.ReadBlueprintList);
        modRegistrationData.RegisterServerComms("steveman0.SendNewBlueprintToClient", Blueprint.SendNewBlueprintName, Blueprint.ReadNewBlueprintName);

        UIManager.NetworkCommandFunctions.Add(BlueprintBuilderWindow.InterfaceName, new UIManager.HandleNetworkCommand(BlueprintBuilderWindow.HandleNetworkCommand));

        if (!PersistentSettings.mbHeadlessServer)
            HoloInstancers = new Instancer[(int)HoloInstancerTypes.InstancerCount];

        if (WorldScript.mbIsServer)
            BlueprintPath = DiskWorld.GetWorldsDir() + Path.DirectorySeparatorChar + WorldScript.instance.mWorldData.mPath + Path.DirectorySeparatorChar + BlueprintBuilderMod.BLUEPRINT_FOLDER + Path.DirectorySeparatorChar;

        Debug.Log("Blueprint Builder Mod v1 Registered");

        return modRegistrationData;
    }

    public override ModItemActionResults PerformItemAction(ModItemActionParameters parameters)
    {
        ModItemActionResults results = new ModItemActionResults();
        if (parameters.ItemToUse.mnItemID == BlueprintMakerID)
        {
            results.Consume = false;
            ModManager.OpenModUI(makerWindow);
            this.LoadBlueprints();
        }
        return results;
    }

    public override ModCreateSegmentEntityResults CreateSegmentEntity(ModCreateSegmentEntityParameters parameters)
    {
        ModCreateSegmentEntityResults results = new ModCreateSegmentEntityResults();

        if (parameters.Cube == BuilderType)
        {
            parameters.ObjectType = SpawnableObjectEnum.FALCOR_MK2;
            results.Entity = new BlueprintBuilder(parameters);
            this.LoadBlueprints();
        }

        return results;
    }

    public void LoadBlueprints()
    {
        if (this.Loaded)
            return;         

        if (WorldScript.mbIsServer)
        {
            if (Directory.Exists(BlueprintBuilderMod.BlueprintPath) == false)
                Directory.CreateDirectory(BlueprintBuilderMod.BlueprintPath);

            string[] BlueprintFiles = Directory.GetFiles(BlueprintBuilderMod.BlueprintPath, "BP_*.dat", SearchOption.TopDirectoryOnly);

            foreach (string filename in BlueprintFiles)
            {
                new Blueprint().LoadBlueprint(filename);
            }
        }
        else
        {
            //request blueprint list
            Debug.Log("Client requesting the blueprint list from the server");
            ModManager.ModSendClientCommToServer("steveman0.RequestListBlueprint");
        }
        this.Loaded = true;
    }

    void Update()
    {
        InstancerConfig();
#if UNITY_EDITOR
            return;
#endif

        if (StartEffect == null)
            StartEffect = GameObject.Find("ScanEffect").GetComponent<ParticleSystem>();
        if (EndEffect == null)
             EndEffect = GameObject.Find("ScanEffect").GetComponent<ParticleSystem>();
        if (BlueprintZone == null)
            BlueprintZone = Instantiate(SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.AutoExcavator].transform.Search("Preview Cube").gameObject);


        CubeCoord Zonestart = BlueprintMakerWindow.Start;
        CubeCoord Zoneend = BlueprintMakerWindow.Finish;
        if (ZoneActive && (Zonestart != CubeCoord.Invalid || Zoneend != CubeCoord.Invalid) && StartEffect != null && EndEffect != null)
        {
            if (Zonestart != CubeCoord.Invalid)
            {
                Vector3 pos = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(Zonestart.x, Zonestart.y, Zonestart.z);
                StartEffect.transform.position = pos + new Vector3(0.5f, 0.5f, 0.5f);
                StartEffect.Emit(1);
            }
            if (Zoneend != CubeCoord.Invalid)
            {
                Vector3 pos = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(Zoneend.x, Zoneend.y, Zoneend.z);
                EndEffect.transform.position = pos + new Vector3(0.5f, 0.5f, 0.5f);
                EndEffect.Emit(1);
            }

            if (Zonestart != CubeCoord.Invalid && Zoneend != CubeCoord.Invalid && ZoneDirty)
            {
                Vector3 start = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(Zonestart.x, Zonestart.y, Zonestart.z);
                Vector3 finish = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(Zoneend.x, Zoneend.y, Zoneend.z);
                Vector3 unitypos = (start + finish) / 2;
                BlueprintZone.transform.position = unitypos + new Vector3(0.5f, 0.5f, 0.5f);
                BlueprintZone.transform.localScale = new Vector3(Abs(start.x - finish.x) + 1.05f, Abs(start.y - finish.y) + 1.05f, Abs(start.z - finish.z) + 1.05f);
                BlueprintZone.SetActive(true);
            }
            else if (Zonestart == CubeCoord.Invalid || Zoneend == CubeCoord.Invalid)
                BlueprintZone.SetActive(false);
        }
        ZoneTimer -= Time.deltaTime;
        if (ZoneTimer < 0)
            ZoneActive = false;
        if (!ZoneActive)
            BlueprintZone.SetActive(false);
    }

    void InstancerConfig()
    {
        if (!PersistentSettings.mbHeadlessServer)
        {
            if (HologramCubes.HoloMaterial == null)
                HologramCubes.HoloMaterial = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.CentralPowerHub_MB].transform.Search("ConveyorBelt").gameObject.GetComponent<Renderer>().material;
            if (HologramCubes.HoloCube == null)
                HologramCubes.HoloCube = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.MassStorageOutputPort].transform.Search("HoloCube").gameObject;

            if (HologramCubes.HoloMaterial != null && HologramCubes.HoloCube != null && HologramCubes.meshes != null && HologramCubes.meshes[3] == null)
            {
                Instancer conveyorinstancer = new Instancer();
                Material conveyormaterial = new Material(InstanceManager.instance.maSimpleMaterials[(int)InstanceManager.eSimpleInstancerType.eT1ConveyorBase]);
                Mesh conveyormesh = InstanceManager.instance.maSimpleMeshes[(int)InstanceManager.eSimpleInstancerType.eT1ConveyorBase];
                Texture holder = conveyormaterial.mainTexture;
                //conveyormaterial.CopyPropertiesFromMaterial(HologramCubes.HoloMaterial);
                //conveyormaterial.mainTexture = holder;
                Material holo = new Material(HologramCubes.HoloMaterial);
                holo.mainTexture = holder;
                HologramCubes.materials[(int)HoloInstancerTypes.ConveyorBelt] = holo;
                HologramCubes.meshes[(int)HoloInstancerTypes.ConveyorBelt] = conveyormesh;
                //conveyorinstancer.Init(conveyormesh, conveyormaterial);

                Instancer PSBInstancer = new Instancer();
                Material psbmaterial = new Material(InstanceManager.instance.maSimpleMaterials[(int)InstanceManager.eSimpleInstancerType.ePSBHull]);
                Mesh psbmesh = InstanceManager.instance.maSimpleMeshes[(int)InstanceManager.eSimpleInstancerType.ePSBHull];
                holder = psbmaterial.mainTexture;
                //psbmaterial.CopyPropertiesFromMaterial(HologramCubes.HoloMaterial);
                //psbmaterial.mainTexture = holder;
                Material holoPSB = new Material(HologramCubes.HoloMaterial);
                holoPSB.mainTexture = holder;
                HologramCubes.materials[(int)HoloInstancerTypes.PSB] = holoPSB;
                HologramCubes.meshes[(int)HoloInstancerTypes.PSB] = psbmesh;
                //PSBInstancer.Init(psbmesh, holoPSB);

                Instancer trackinstancer = new Instancer();
                Material TrackMaterial = new Material(InstanceManager.instance.maSimpleMaterials[(int)InstanceManager.eSimpleInstancerType.eMinecartStraight]);
                Mesh TrackMesh = InstanceManager.instance.maSimpleMeshes[(int)InstanceManager.eSimpleInstancerType.eMinecartStraight];
                holder = TrackMaterial.mainTexture;
                //TrackMaterial.CopyPropertiesFromMaterial(HologramCubes.HoloMaterial);
                //TrackMaterial.mainTexture = holder;
                Material holotrack = new Material(HologramCubes.HoloMaterial);
                holotrack.mainTexture = holder;
                HologramCubes.materials[(int)HoloInstancerTypes.TrackStraight] = holotrack;
                HologramCubes.meshes[(int)HoloInstancerTypes.TrackStraight] = TrackMesh;
                //trackinstancer.Init(TrackMesh, holotrack);

                Instancer cubeinstancer = new Instancer();
                Material cubematerial = new Material(HologramCubes.HoloCube.GetComponent<Renderer>().material);
                Mesh cubemesh = HologramCubes.HoloCube.GetComponent<MeshFilter>().mesh;
                holder = cubematerial.mainTexture;
                //cubematerial.CopyPropertiesFromMaterial(HologramCubes.HoloMaterial);
                //cubematerial.mainTexture = holder;
                Material holocube = new Material(HologramCubes.HoloMaterial);
                holocube.mainTexture = holder;
                HologramCubes.materials[(int)HoloInstancerTypes.Cube] = holoPSB;
                HologramCubes.meshes[(int)HoloInstancerTypes.Cube] = psbmesh;
                //cubeinstancer.Init(cubemesh, holocube);

                //HoloInstancers[(int)HoloInstancerTypes.ConveyorBelt] = conveyorinstancer;
                //HoloInstancers[(int)HoloInstancerTypes.PSB] = PSBInstancer;
                //HoloInstancers[(int)HoloInstancerTypes.TrackStraight] = trackinstancer;
                //HoloInstancers[(int)HoloInstancerTypes.Cube] = cubeinstancer;
            }
        }
    }

    //void LateUpdate()
    //{
    //    if (PersistentSettings.mbHeadlessServer) return;
    //    for (int n = 0; n < (int)HoloInstancerTypes.InstancerCount; n++)
    //        HoloInstancers[n].Render();
    //}

    public enum HoloInstancerTypes
    {
        ConveyorBelt,
        PSB,
        TrackStraight,
        Cube,
        InstancerCount,
    }
}

