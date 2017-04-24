using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class HologramCubes
{
    public static Material HoloMaterial;
    public static GameObject HoloCube;
    public static Material[] materials = new Material[4];
    public static Mesh[] meshes = new Mesh[4];

    /// <summary>
    /// Returns a hologram instance of a machine entity for the provided cubetype at the specified position and rotation
    /// </summary>
    /// <param name="cubetype">Cube type of the block/machine</param>
    /// <param name="pos">Position to place the hologram</param>
    /// <param name="rotation">rotation of the hologram</param>
    /// <returns></returns>
    public static GameObject GetHologramFromCube(ushort cubetype, ushort val, Vector3 pos, Quaternion rotation)
    {
        //Debug.Log("Making hologram for cube type: " + cubetype + " with val " + val);
        List<string> exclusions = new List<string>();
        SpawnableObjectEnum objnum = GetObjectData(cubetype, val, out exclusions);

        if (objnum == SpawnableObjectEnum.XXX)
            return null;
        else if (objnum == SpawnableObjectEnum.XXXX)
        {
            GameObject holocube = Object.Instantiate(HoloCube, pos + new Vector3(0.5f, 0.5f, 0.5f), rotation);
            Object.Destroy(holocube.GetComponent<RotateConstantlyScript>());
            holocube.transform.localScale *= 2;
            return ConfigureGO(holocube, objnum, exclusions);
        }
        GameObject ob = ConfigureGO(Object.Instantiate(SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)objnum], pos + new Vector3(0.5f, 0.5f, 0.5f), rotation), objnum, exclusions);
        //AutoHoloInstancer inst = ob.GetComponent<AutoHoloInstancer>();
        //if (inst != null)
        //{
        //    if (objnum == SpawnableObjectEnum.Conveyor)
        //        inst.mType = BlueprintBuilderMod.HoloInstancerTypes.ConveyorBelt;
        //    else if (objnum == SpawnableObjectEnum.Minecart_Track_Straight)
        //        inst.mType = BlueprintBuilderMod.HoloInstancerTypes.TrackStraight;
        //    else if (objnum == SpawnableObjectEnum.XXXX)
        //        inst.mType = BlueprintBuilderMod.HoloInstancerTypes.Cube;
        //    else
        //        inst.mType = BlueprintBuilderMod.HoloInstancerTypes.PSB;

        //    inst.position = pos;
        //    inst.rotation = rotation;
        //    inst.enabled = true;
        //    inst.gameObject.SetActive(true);
        //}
        NonInstancedHolo holo = ob.GetComponent<NonInstancedHolo>();
        if (holo != null)
        {
            float offset = 0f;
            if (objnum == SpawnableObjectEnum.Conveyor)
            {
                holo.mType = BlueprintBuilderMod.HoloInstancerTypes.ConveyorBelt;
                offset = -0.2174988f;
            }
            else if (objnum == SpawnableObjectEnum.Minecart_Track_Straight)
                holo.mType = BlueprintBuilderMod.HoloInstancerTypes.TrackStraight;
            else if (objnum == SpawnableObjectEnum.XXXX)
                holo.mType = BlueprintBuilderMod.HoloInstancerTypes.Cube;
            else
                holo.mType = BlueprintBuilderMod.HoloInstancerTypes.PSB;

            holo.position = pos + new Vector3(0.5f, 0.5f, 0.5f) + rotation * Vector3.up * offset;
            holo.rotation = rotation;
            holo.enabled = true;
            holo.gameObject.SetActive(true);
        }

        return ob;
    }

    /// <summary>
    /// Applies the hologram material to the object and disables undesired meshes
    /// </summary>
    /// <param name="obj">The GameObject to configure</param>
    /// <param name="exclusions">List of mesh names to disable for the object</param>
    /// <returns>The object reference again, just because</returns>
    private static GameObject ConfigureGO(GameObject obj, SpawnableObjectEnum objnum, List<string> exclusions)
    {
        Renderer[] ren = obj.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in ren)
        {
            //Debug for getting renderer names for building exclusions list
            if (Input.GetKey(KeyCode.LeftAlt))
                Debug.Log("Building blueprint with obj: " + obj.name + " with renderer: " + r.name);
            if (exclusions.Contains(r.name))
            {
                r.enabled = false;
                continue;
            }

            Texture holder = r.material.mainTexture;
            r.material.CopyPropertiesFromMaterial(HoloMaterial);
            r.material.shader = HoloMaterial.shader;
            r.material.mainTexture = holder;
        }

        //Conditional removals!
        switch (objnum)
        {
            case SpawnableObjectEnum.PowerStorageBlock:
                Object.Destroy(obj.GetComponent<AudioSource>());
                break;
            case SpawnableObjectEnum.PowerStorageBlock_MK4:
                Object.Destroy(obj.GetComponent<AudioSource>());
                break;
            case SpawnableObjectEnum.PowerStorageBlock_MK5:
                Object.Destroy(obj.GetComponent<AudioSource>());
                break;
            case SpawnableObjectEnum.Minecart_Track_Straight:
                Object.Destroy(obj.GetComponent<AutoUnityInstancer>());
                break;
        }
        if (Input.GetKey(KeyCode.LeftAlt))
            DebugObjectChildren(obj);

        if (objnum == SpawnableObjectEnum.Conveyor ||
            objnum == SpawnableObjectEnum.Minecart_Track_Straight ||
            objnum == SpawnableObjectEnum.PowerStorageBlock ||
            objnum == SpawnableObjectEnum.PowerStorageBlock_T2 ||
            objnum == SpawnableObjectEnum.PowerStorageBlock_T3)
        {
            //obj.AddComponent<AutoHoloInstancer>();
            obj.AddComponent<NonInstancedHolo>();
        }

        obj.SetActive(true);
        return obj;
    }

    private static void DebugObjectChildren(GameObject obj)
    {
        Component[] objs = obj.GetComponents<Component>();
        Debug.Log("---------------GameObject Debug---------------------");
        Debug.Log("Object name: " + obj.name);
        foreach (Component comp in objs)
        {
            Debug.Log("Component named: " + comp.name + " of type: " + comp.GetType());
        }
    }

    /// <summary>
    /// Gets the object for the cube type and the list of undesired meshes associated with it
    /// </summary>
    /// <param name="type">Cube type of the object</param>
    /// <param name="names">List of the mesh names that should be turned off to keep only the base mesh</param>
    /// <returns>The spawnable object enum associated with the cube</returns>
    private static SpawnableObjectEnum GetObjectData(ushort type, ushort val, out List<string> names)
    {
        names = new List<string>();
        switch (type)
        {
            case 1:
                return SpawnableObjectEnum.XXX; // No object
            case eCubeTypes.LaserPowerTransmitter:
                names.Add("LaserTransfer");
                names.Add("Laser Quad");
                names.Add("Laser Impact");
                return SpawnableObjectEnum.LaserPowerTransmitter;
            case eCubeTypes.AIMover:
                return SpawnableObjectEnum.AIMover;
            case eCubeTypes.ARTHERRechargeStation:
                return SpawnableObjectEnum.ARTHERRechargeStation;
            case eCubeTypes.ARTHERTurret:
                return SpawnableObjectEnum.ARTHER_Turret;
            case eCubeTypes.AutoBuilder:
                return SpawnableObjectEnum.AutoBuilder;
            case eCubeTypes.AutoExcavator:
                return SpawnableObjectEnum.AutoExcavator;
            case eCubeTypes.AutoOreRemover:
                return val == 1 ? SpawnableObjectEnum.AutoOreRemover : SpawnableObjectEnum.XXX;
            case eCubeTypes.AutoOrganicThief:
                return val == 1 ? SpawnableObjectEnum.RuinedOrganicThief : SpawnableObjectEnum.XXX;
            case eCubeTypes.AutoUpgrader:
                return SpawnableObjectEnum.AutoUpgrader;
            case eCubeTypes.BFL9000:
                return SpawnableObjectEnum.BFL9000;
            case eCubeTypes.BlastFurnace:
                return val == 1 ? SpawnableObjectEnum.BlastFurnace : SpawnableObjectEnum.XXX;
            case eCubeTypes.CargoLiftController:
                return val == 1 ? SpawnableObjectEnum.CargoLiftController : SpawnableObjectEnum.XXX;
            case eCubeTypes.CastingPipe:
                return SpawnableObjectEnum.CastingPipe;
            case eCubeTypes.CentralPowerHub:
                return val == 1 ? SpawnableObjectEnum.CentralPowerHub : SpawnableObjectEnum.XXX;
            case eCubeTypes.ConfigurableLight:
                return SpawnableObjectEnum.ElectricLight;
            case eCubeTypes.ContinuousCastingBasin:
                return val == 1 ? SpawnableObjectEnum.ContinuousCastingBasin : SpawnableObjectEnum.XXX;
            case eCubeTypes.Conveyor:
                switch (val)
                {
                    case 1:
                        return SpawnableObjectEnum.Conveyor_Filter_Single;
                    case 2:
                        return SpawnableObjectEnum.TransportPipe;
                    case 3:
                        return SpawnableObjectEnum.TransportPipe_Filter_Single;
                    case 4:
                        names.Add("Moving Item Obj");
                        return SpawnableObjectEnum.Stamper_T1;
                    case 5:
                        names.Add("Moving Item Obj");
                        return SpawnableObjectEnum.Extruder_T1;
                    case 6:
                        return SpawnableObjectEnum.Coiler_T1;
                    case 8:
                        return SpawnableObjectEnum.PipeExtruder_T1;
                    case 9:
                        return SpawnableObjectEnum.PCBAssembler_T1;
                    case 10:
                        return SpawnableObjectEnum.Turntable_T1;
                    case 12:
                        return SpawnableObjectEnum.Conveyor_Filter_Advanced;
                    case 13:
                        return SpawnableObjectEnum.Conveyor_SlopeUp;
                    case 14:
                        return SpawnableObjectEnum.Conveyor_SlopeDown;
                    case 15:
                        return SpawnableObjectEnum.Conveyor_Motorised;
                    case 0:
                    case 11:
                    default:
                        names.Add("Moving Item Obj");
                        names.Add("ConveyorBelt");
                        names.Add("ConveyorObject");
                        return SpawnableObjectEnum.Conveyor;
                }
            case eCubeTypes.CryoMine:
                return SpawnableObjectEnum.CryoMine;
            case eCubeTypes.Crystal:
                return SpawnableObjectEnum.Crystal_Diamond;
            case eCubeTypes.ElectricLight:
                return SpawnableObjectEnum.ElectricLight;
            case eCubeTypes.EmergencyLight:
                return SpawnableObjectEnum.EmergencyRedLight;
            case eCubeTypes.FALCOR:
                return SpawnableObjectEnum.FALCOR_MK1;
            case eCubeTypes.FALCOR_Beacon:
                return SpawnableObjectEnum.FALCOR_Beacon;
            case eCubeTypes.FALCOR_Bomber:
                return SpawnableObjectEnum.FALCOR_MK3;
            case eCubeTypes.ForcedInduction:
                return SpawnableObjectEnum.AirInductor;
            case eCubeTypes.FreezonInjector:
                return SpawnableObjectEnum.FreezonInjector;
            case eCubeTypes.GeologicalSurveyor:
                return SpawnableObjectEnum.GeologicalSurveyor;
            case eCubeTypes.GeothermalGenerator:
                return val == 1 ? SpawnableObjectEnum.GeothermalGenerator : SpawnableObjectEnum.XXX;
            case eCubeTypes.HalloweenCauldron:
                return SpawnableObjectEnum.HalloweenCauldron;
            case eCubeTypes.HardResinDetector:
                return SpawnableObjectEnum.HardResinDetector;
            case eCubeTypes.HeatConductingPipe:
                return SpawnableObjectEnum.HeatConductantPipe;
            case eCubeTypes.Hydroponics:
                return SpawnableObjectEnum.HydroponicsBay;
            case eCubeTypes.InductionCharger:
                return val == 1 ? SpawnableObjectEnum.InductionCharger : SpawnableObjectEnum.XXX;
            case eCubeTypes.ItemInjector:
                return SpawnableObjectEnum.ItemInjector;
            case eCubeTypes.JetTurbineGenerator:
                return val == 1 ? SpawnableObjectEnum.JetTurbine : SpawnableObjectEnum.XXX;
            case eCubeTypes.Laboratory:
                return val == 1 ? SpawnableObjectEnum.Laboratory : SpawnableObjectEnum.XXX;
            case eCubeTypes.LaserAndGate:
                return SpawnableObjectEnum.LaserAndGate;
            case eCubeTypes.LaserEmitter:
                return SpawnableObjectEnum.LaserEmitter;
            case eCubeTypes.LaserMirror:
                return SpawnableObjectEnum.LaserMirror;
            case eCubeTypes.LaserNotGate:
                return SpawnableObjectEnum.LaserNotGate;
            case eCubeTypes.LaserOrGate:
                return SpawnableObjectEnum.LaserOrGate;
            case eCubeTypes.LaserPressurePad:
                return SpawnableObjectEnum.LaserPressurePad;
            case eCubeTypes.LaserReceptor:
                return SpawnableObjectEnum.LaserReciever;
            case eCubeTypes.LaserResinAblator:
                return SpawnableObjectEnum.LaserAblator;
            case eCubeTypes.LaserResinLiquifier:
                return SpawnableObjectEnum.LaserLiquifier;
            case eCubeTypes.LaserSplitter:
                return SpawnableObjectEnum.LaserSplitter;
            case eCubeTypes.Lift_Compressor:
                return SpawnableObjectEnum.Lift_Compressor;
            case eCubeTypes.Lift_ManualControl:
                return SpawnableObjectEnum.Lift_ManualControl;
            case eCubeTypes.Macerator:
                return SpawnableObjectEnum.Macerator;
            case eCubeTypes.ManufacturingPlant:
                return SpawnableObjectEnum.ManufacturingPlantCore;
            case eCubeTypes.ManufacturingPlantModule:
                return SpawnableObjectEnum.HydrojetCutter;
            case eCubeTypes.MassStorageController:
                return SpawnableObjectEnum.MassStorageIOController;
            case eCubeTypes.MassStorageCrate:
                names.Add("StorageCrate");
                return SpawnableObjectEnum.MassStorageCrate;
            case eCubeTypes.MassStorageInputPort:
                names.Add("Thrust");
                names.Add("DigiTransmit");
                names.Add("NoItemSet");
                names.Add("End");
                return SpawnableObjectEnum.MassStorageInputPort;
            case eCubeTypes.MassStorageIOPort:
                return SpawnableObjectEnum.MassStorageIOPort;
            case eCubeTypes.MassStorageOutputPort:
                names.Add("Thrust");
                names.Add("DigiTransmit");
                names.Add("NoItemSet");
                names.Add("End");
                return SpawnableObjectEnum.MassStorageOutputPort;
            case eCubeTypes.MatterMover:
                return SpawnableObjectEnum.MatterMover;
            case eCubeTypes.MinecartControl:
                switch (val)
                {
                    case 0:
                        return SpawnableObjectEnum.Minecart_Track_Factory;
                    case 1:
                        return SpawnableObjectEnum.Minecart_Track_Brake;
                    case 2:
                        return SpawnableObjectEnum.Minecart_Track_Boost;
                    case 3:
                        return SpawnableObjectEnum.Minecart_Track_UnloadStation;
                    case 4:
                        return SpawnableObjectEnum.Minecart_Track_LoadStation;
                }
                return SpawnableObjectEnum.Minecart_Track_Boost;
            case eCubeTypes.MinecartTrack:
                switch (val)
                {
                    case 0:
                        return SpawnableObjectEnum.Minecart_Track_Straight;
                    case 1:
                        return SpawnableObjectEnum.Minecart_Track_Corner;
                    case 2:
                        return SpawnableObjectEnum.Minecart_Track_Slope;
                    case 3:
                        return SpawnableObjectEnum.Minecart_Track_Buffer;
                    case 4:
                        return SpawnableObjectEnum.Minecart_Track_BufferFull;
                    case 5:
                        return SpawnableObjectEnum.Minecart_Track_BufferEmpty;
                }
                return SpawnableObjectEnum.Minecart_Track_Straight;
            case eCubeTypes.MissileTurret:
                return SpawnableObjectEnum.MissileTurret_T1;
            case eCubeTypes.MK1RobotArm:
                return SpawnableObjectEnum.MK1RobotArm;
            case eCubeTypes.OrbitalEnergyTransmitter:
                return val == 1 ? SpawnableObjectEnum.OrbitalEnergyTransmitter : SpawnableObjectEnum.XXX;
            case eCubeTypes.OrbitalStrikeController:
                return SpawnableObjectEnum.OrbitalStrikeController;
            case eCubeTypes.OreExtractor:
                return SpawnableObjectEnum.OreExtractor;
            case eCubeTypes.OreSmelter:
                return SpawnableObjectEnum.OreSmelter;
            case eCubeTypes.PopupTurret:
                return SpawnableObjectEnum.PopUpTurret;
            case eCubeTypes.PowerStorageBlock:
                names.Add("Sphere_High");
                names.Add("Sphere_Low");
                names.Add("Battery Glow Bar");
                return SpawnableObjectEnum.PowerStorageBlock;
            case eCubeTypes.PowerStorageBlock_T4:
                return val == 1 ? SpawnableObjectEnum.PowerStorageBlock_MK4 : SpawnableObjectEnum.XXX;
            case eCubeTypes.PowerStorageBlock_T5:
                return val == 1 ? SpawnableObjectEnum.PowerStorageBlock_MK5 : SpawnableObjectEnum.XXX;
            case eCubeTypes.PumpkinTorch:
                return SpawnableObjectEnum.Pumpkin_Torch;
            case eCubeTypes.PyrothermicGenerator:
                return SpawnableObjectEnum.PyrothermicGenerator;
            case eCubeTypes.Quarry:
                return SpawnableObjectEnum.Quarry;
            case eCubeTypes.RackRail:
                return SpawnableObjectEnum.RackRail;
            case eCubeTypes.RefineryController:
                return SpawnableObjectEnum.RefineryController;
            case eCubeTypes.RefineryReactorVat:
                return val == 1 ? SpawnableObjectEnum.RefineryReactorVat : SpawnableObjectEnum.XXX;
            case eCubeTypes.ResearchAssembler:
                return SpawnableObjectEnum.ExperimentalAssembler;
            case eCubeTypes.ResearchStation:
                return SpawnableObjectEnum.ResearchStation;
            case eCubeTypes.RocketComponent:
                return SpawnableObjectEnum.BasicRocketFramework;
            case eCubeTypes.ServerMonitor:
                return SpawnableObjectEnum.ServerMonitor;
            case eCubeTypes.Sign:
                return SpawnableObjectEnum.Sign;
            case eCubeTypes.SlimeAttractor:
                return SpawnableObjectEnum.SlimeAttractor;
            case eCubeTypes.SnowmanTorch:
                return SpawnableObjectEnum.Snowman_Torch;
            case eCubeTypes.SolarPanel:
                return SpawnableObjectEnum.SolarPanel_T1;
            case eCubeTypes.SolarPanel_MK2:
                return val == 1 ? SpawnableObjectEnum.SolarPanel_T2 : SpawnableObjectEnum.XXX;
            case eCubeTypes.SolarPanel_MK2_Organic:
                return val == 1 ? SpawnableObjectEnum.SolarPanel_T2_Organic : SpawnableObjectEnum.XXX;
            case eCubeTypes.SpiderBotBase:
                return val == 1 ? SpawnableObjectEnum.SpiderBotBase : SpawnableObjectEnum.XXX;
            case eCubeTypes.StorageHopper:
                names.Add("HooverGraphic");
                switch (val)
                {
                    case 1:
                        return SpawnableObjectEnum.MicroHopper;
                    case 2:
                        return SpawnableObjectEnum.LogisticsHopper;
                    case 3:
                        return SpawnableObjectEnum.CryoHopper;
                    case 4:
                        return SpawnableObjectEnum.MotorisedLogisticsHopper;
                }
                return SpawnableObjectEnum.StorageHopper;
            case eCubeTypes.T1_Lift:
                return SpawnableObjectEnum.Lift_T1;
            case eCubeTypes.T3_FuelCompressor:
                return val == 1 ? SpawnableObjectEnum.T3_FuelCompressor : SpawnableObjectEnum.XXX;
            case eCubeTypes.T3_SLODRS:
                return val == 1 ? SpawnableObjectEnum.SLODRS_Base : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_CCCCC:
                return val == 1 ? SpawnableObjectEnum.CCCCC : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_Conduit:
                return val == 1 ? SpawnableObjectEnum.T4_Conduit : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_CreepBurner:
                return val == 1 ? SpawnableObjectEnum.T4_CreepBurner : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_CreepInferno:
                return val == 1 ? SpawnableObjectEnum.T4_CreepInferno : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_GasBottler:
                return val == 1 ? SpawnableObjectEnum.T4_GasBottler : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_GasStorage:
                return val == 1 ? SpawnableObjectEnum.T4_GasStorage : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_GenericPipe:
                return SpawnableObjectEnum.CastingPipe;
            case eCubeTypes.T4_Grinder:
                return val == 1 ? SpawnableObjectEnum.T4_Grinder : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_Lancer:
                return SpawnableObjectEnum.Creep_Lancer;
            case eCubeTypes.T4_LaserBorer:
                return val == 1 ? SpawnableObjectEnum.T4_LaserBorer : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_MagmaBore:
                return val == 1 ? SpawnableObjectEnum.MagmaBore : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_MagmaStorage:
                return val == 1 ? SpawnableObjectEnum.T4_MagmaStorage : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_Mortar:
                return SpawnableObjectEnum.Creep_Mortar;
            case eCubeTypes.T4_ParticleCompressor:
                return val == 1 ? SpawnableObjectEnum.T4_ParticleCompressor : SpawnableObjectEnum.XXX;
            case eCubeTypes.T4_ParticleFilter:
                return val == 1 ? SpawnableObjectEnum.T4_ParticleFilter : SpawnableObjectEnum.XXX;
            case eCubeTypes.Teleporter:
                return SpawnableObjectEnum.Teleporter;
            case eCubeTypes.ThreatScanner:
                return SpawnableObjectEnum.ThreatScanner;
            case eCubeTypes.Torch:
                return SpawnableObjectEnum.TorchFloor;
            case eCubeTypes.TrackGate:
                return SpawnableObjectEnum.TrackGate;
            case eCubeTypes.TrackPoint:
                return SpawnableObjectEnum.TrackPoint;
            case eCubeTypes.TrackStation:
                return SpawnableObjectEnum.TrackStation;
            case eCubeTypes.TrackTerminus:
                return SpawnableObjectEnum.TrackTerminus;
            case eCubeTypes.Turret_MK4:
                return val == 1 ? SpawnableObjectEnum.Turret_T4 : SpawnableObjectEnum.XXX;
            case eCubeTypes.WarningLight:
                return SpawnableObjectEnum.EmergencyRedLight;
            case eCubeTypes.Wasp_Agitator:
                return SpawnableObjectEnum.Wasp_Agitator;
            case eCubeTypes.Wasp_Calmer:
                return SpawnableObjectEnum.Wasp_Calmer;
            case eCubeTypes.WaypointMachine:
                return SpawnableObjectEnum.WaypointMachine;
            case eCubeTypes.WorkFloorExcavator:
                return SpawnableObjectEnum.WorkFloorExcavator;
            case eCubeTypes.ZipperMerge:
                return SpawnableObjectEnum.Zipper_Merge;
            default:
                return SpawnableObjectEnum.XXXX; // Dummy for indicating use the holocube
        }
    }
}

