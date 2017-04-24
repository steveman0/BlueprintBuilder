using System;
using System.Collections.Generic;
using FortressCraft.Community.Utilities;
using System.Linq;
using UnityEngine;
using System.IO;

public class BlueprintBuilder : MachineEntity, FreightSystemInterface, PowerConsumerInterface
{
    #region Fields
    // Storage
    private object ItemsLock = new object();
    private object ExtraLock = new object();
    public List<FreightListing> NeededItems;
    public List<FreightListing> ExtraItems;
    private int HopperCheckIndex;
    public MachineInventory LocalStorage;
    public const int LocalMAXStorage = 50;
    public ItemBase mCarriedItem;

    //Blueprint management
    public Blueprint CurrentBlueprint;
    public GameObject[,,] Hologram;
    public bool[,,] BlockPlaced;
    public int TotalBlocks;  // Total blocks to be placed by this blueprint
    public int NumberPlaced; // Number placed so far -> for calculating % complete
    private const int HoloFrameCap = 500;   // Number of hologram objects generated per frame at cap
    public bool HoloDirty;
    public bool HoloBuilding;
    public BuildState BuildMode;
    public BuildType BuildAllowance;
    public RotState BPRotation;
    public bool BPMirroredX;
    public bool BPMirroredZ;
    public Vector3 BuildDirection;
    public List<CubeCoord> LockedCoords;
    public CubeCoord TargetIndex;
    private int xHG;  // Coordinates of round robin hologram rendering
    private int yHG;
    private int zHG;
    private int xcBP;  // Coordinates of round robin configuring of blueprint
    private int ycBP;
    private int zcBP;
    
    //Connections and setup
    public StorageMachineInterface AttachedHopper;
    public BlueprintBuilder AttachedBuilder;
    public eConnectionType ConnectedMachine;
    private float FreightTimer;
    public Vector3 Forwards;
    public bool mbLinkedToGO;
    private bool BuildFromSave = false;  // Tells the builder to start building if it loaded in and was previously building

    // Falcor objects
    GameObject DroneObject;
    GameObject EngineObject;
    GameObject ThrustObject;
    AudioSource ThrustAudio;
    Light WorkLight;
    ParticleSystem Sparks;
    private bool ReadyToDock;
    // Add hologram hopper to show attach point?

    // Falcor movement
    public eState mState;
    private eFlyState mFlyState;
    private RaycastRequest mRequest;
    public Vector3 mVisDroneOffset;
    public Vector3 mDroneOffset;
    public Vector3 mVTT;
    public Vector3 mTargetDroneOffset;
    private Vector3 mVisualDroneLocation;
    public float mrStateTimer;
    public CubeCoord TargetCoord;
    public int mnOurRise;
    public int mnTargetRise;

    // Power
    private const float MaxPower = 500f;
    private const float MaxDelivery = 100000f;
    public float CurrentPower;
    public float TargetPower;       // Amount of power required to build the current block
    public float PowerProgress;     // Power already devoted to constructions for the current block
    public int BPPowerCost;
    public const int PowerPerDistance = 1; // Power cost per taxicab distance from builder
    public const int PowerPerMachine = 100;
    #endregion

    public BlueprintBuilder(ModCreateSegmentEntityParameters parameters)
        : base(parameters)
    {
        this.mbNeedsLowFrequencyUpdate = true;
        this.mbNeedsUnityUpdate = true;
        this.Forwards = SegmentCustomRenderer.GetRotationQuaternion(parameters.Flags) * Vector3.forward;
        this.Forwards.Normalize();
        this.BuildDirection = SegmentCustomRenderer.GetRotationQuaternion(parameters.Flags) * Vector3.right;
        this.BuildDirection.Normalize();
        this.LocalStorage = new MachineInventory(this, LocalMAXStorage);
        this.ReadyToDock = true;
        this.NeededItems = new List<FreightListing>();
        this.ExtraItems = new List<FreightListing>();
    }


    #region LowFrequencyCore
    public override void LowFrequencyUpdate()
    {
        this.CheckMachineConnections();
        this.HandleBuildPowerUpdate();
        this.HandleHopper();
        this.CheckParentBuilder();

        switch (this.BuildMode)
        {
            case BuildState.AwaitingStart:
            case BuildState.Blocked:
            case BuildState.Idle:
                this.mDroneOffset *= 0.9f;
                break;
            case BuildState.ConfiguringBlueprint:
                this.mDroneOffset *= 0.9f;
                this.ConfiguringBlueprint();
                break;
            case BuildState.Building:
                this.HandleBlueprintBuilding();
                break;
        }
    }

    private void CheckMachineConnections()
    {
        this.FreightTimer -= LowFrequencyThread.mrPreviousUpdateTimeStep;
        if (this.ConnectedMachine == eConnectionType.Freight && this.FreightTimer < 0)
            this.ConnectedMachine = eConnectionType.None;
        else if (this.ConnectedMachine != eConnectionType.Freight && this.FreightTimer > 0)
            this.ConnectedMachine = eConnectionType.Freight;

        if (this.ConnectedMachine == eConnectionType.Hopper && this.AttachedHopper == null)
            this.ConnectedMachine = eConnectionType.None;

        if (this.ConnectedMachine == eConnectionType.Builder && (this.AttachedBuilder == null || this.AttachedBuilder.mbDelete))
        {
            this.AttachedBuilder = null;
            this.ConnectedMachine = eConnectionType.None;
        }
        if (this.ConnectedMachine == eConnectionType.None)
            this.AttachMachines();
    }

    private void CheckParentBuilder()
    {
        if (this.ConnectedMachine != eConnectionType.Builder)
            return;
        //Update state appropriate to the controlling builder
        BlueprintBuilder builder = this.GetControllingBuilder();
        this.BuildMode = builder.BuildMode;
    }

    private void HandleBuildPowerUpdate()
    {
        if (this.PowerProgress < this.TargetPower)
        {
            float powertogo = this.TargetPower - this.PowerProgress;
            if (this.CurrentPower >= powertogo)
            {
                this.CurrentPower -= powertogo;
                this.PowerProgress = this.TargetPower;
            }
            else
            {
                this.PowerProgress += this.CurrentPower;
                this.CurrentPower = 0;
            }
        }
    }

    private void AttachMachines()
    {
        //Get a hopper or builder from the forwards to attach to
        long mnX = this.mnX;
        long mnY = this.mnY;
        long mnZ = this.mnZ;
        mnX += (long)this.Forwards.x;
        mnZ += (long)this.Forwards.z;

        Segment segment = this.AttemptGetSegment(mnX, mnY, mnZ);
        if (segment != null)
        {
            SegmentEntity entity = segment.SearchEntity(mnX, mnY, mnZ);

            StorageMachineInterface machineInterface = entity as StorageMachineInterface;
            if (machineInterface != null)
            {
                eHopperPermissions permissions = machineInterface.GetPermissions();
                if (permissions != eHopperPermissions.Locked && (permissions != eHopperPermissions.AddOnly))
                {
                    this.AttachedHopper = machineInterface;
                    this.ConnectedMachine = eConnectionType.Hopper;
                    return;
                }
            }

            BlueprintBuilder builder = entity as BlueprintBuilder;
            if (builder != null)
            {
                this.AttachedBuilder = builder;
                this.ConnectedMachine = eConnectionType.Builder;
            }
        }
    }

    private void HandleHopper()
    {
        // Handle finding items from at attached hopper or offload excess
        if (this.ConnectedMachine != eConnectionType.Hopper || this.AttachedHopper == null)
        {
            return;
        }

        this.BuildNeededItemsList();
        this.BuildExtraItemsList();
        this.HopperCheckIndex++;
    }

    private void BuildNeededItemsList()
    {
        // Collect needed items
        if (this.NeededItems == null || this.NeededItems.Count == 0)
            return;
        if (this.HopperCheckIndex >= this.NeededItems.Count)
            this.HopperCheckIndex = 0;
        int count = 0;
        FreightListing listitem = this.NeededItems[this.HopperCheckIndex];
        // Keep spare capacity for returning items!
        if (listitem.Quantity > (this.LocalStorage.SpareCapacity() - 15))
            count = this.LocalStorage.SpareCapacity() - 15;
        else
            count = listitem.Quantity;
        if (count < 0)
            count = 0;
        //Debug.Log("Blueprint builder BuildNeededItemsList index " + this.HopperCheckIndex + " listitem " + listitem.Item.ToString() + " and count " + listitem.Quantity.ToString());

        ItemCubeStack stack = listitem.Item as ItemCubeStack;
        if (stack == null)
        {
            Debug.LogWarning("BlueprintBuilder BuildNeededItemsList had a FreightList item that wasn't a cube stack!  Item building not supported!");
            return;
        }
        int amount = this.AttachedHopper.TryPartialExtractCubes(this, stack.mCubeType, stack.mCubeValue, count);
        //Debug.Log("BlueprintBuilder BuildNeededItemsList withdrew item type " + stack.mCubeType + " of attempted count " + count + " and received amount " + amount);
        if (amount > 0)
        {
            ItemBase rem = this.LocalStorage.AddItem(listitem.Item.NewInstance().SetAmount(amount), amount);
            if (rem == null)
            {
                if (listitem.Quantity > amount)
                    listitem.Quantity -= amount;
                else
                    this.NeededItems.Remove(listitem);
            }
            else
            {
                Debug.LogWarning("BlueprintBuilder attempted to add item to local inventory but couldn't fit it.  Doesn't currently cope... returning to hopper");
                this.AttachedHopper.TryPartialInsert(this, ref rem, false, false);
            }
        }
    }

    private void BuildExtraItemsList()
    {
        // Offload excess items
        if (this.ExtraItems == null)
            return;
        if (this.ExtraItems.Count > 0 && this.AttachedHopper.RemainingCapacity > 0)
        {
            int transfer = Math.Min(this.AttachedHopper.RemainingCapacity, this.ExtraItems[0].Quantity);
            ItemBase item = this.LocalStorage.RemovePartialStack(this.ExtraItems[0].Item.NewInstance().SetAmount(transfer));
            transfer = item.GetAmount();
            if (item != null)
            {
                int inserted = this.AttachedHopper.TryPartialInsert(this, ref item, false, false);
                if (inserted > 0)
                {
                    if (inserted >= this.ExtraItems[0].Quantity)
                        this.ExtraItems.RemoveAt(0);
                    else
                        this.ExtraItems[0].Quantity -= inserted;
                }
                if (inserted != transfer)
                {
                    Debug.LogWarning("Blueprint builder tried to offload " + transfer + " but could only add " + inserted + " to the hopper! Returning to inventory...");
                    this.LocalStorage.AddItem(item.NewInstance().SetAmount(transfer - inserted));
                }
            }
        }
    }
    #endregion

    #region BlueprintBuilding
    public void HandleBlueprintBuilding()
    {
        this.mrStateTimer += LowFrequencyThread.mrPreviousUpdateTimeStep;
        if (WorldScript.mbIsServer && (this.TargetCoord == CubeCoord.Invalid || this.GetControllingBuilder().CurrentBlueprint == null) && (this.mState == BlueprintBuilder.eState.eCalculatingRoute || this.mState == BlueprintBuilder.eState.eTravellingToBuildSite))
        {
            Debug.LogWarning("Warning! BlueprintBuilder lost destination; reverting to dock");
            this.SetFalcorState(BlueprintBuilder.eState.eDocked);
        }
        switch (this.mState)
        {
            case BlueprintBuilder.eState.eDocked:
                this.mDroneOffset *= 0.9f;
                if (WorldScript.mbIsServer)
                {
                    if (mCarriedItem == null)
                    {
                        this.SetFalcorState(BlueprintBuilder.eState.eWaitingForItem);
                    }
                    else // If we docked and have an item we probably came back due to a blueprint change or blockage
                        this.SetFalcorState(eState.eLookingToOffloadCargo);
                }
                break;
            case BlueprintBuilder.eState.eWaitingForItem:
                this.mDroneOffset *= 0.9f;
                if (WorldScript.mbIsServer)
                {
                    this.ChooseDestination();
                }
                break;
            case BlueprintBuilder.eState.eCalculatingRoute:
                this.mDroneOffset *= 0.9f;
                if (WorldScript.mbIsServer)
                {
                    this.CalculateRoute();
                }
                break;
            case BlueprintBuilder.eState.eTravellingToBuildSite:
                //if (WorldScript.mbIsServer)
                //{
                //    this.GetControllingBuilder().RequestImmediateNetworkUpdate();
                //}
                if (this.mFlyState == BlueprintBuilder.eFlyState.eRising)
                {
                    this.mVTT = this.mTargetDroneOffset - this.mDroneOffset;
                    if (this.mDroneOffset.y < this.mTargetDroneOffset.y + (float)this.mnTargetRise)
                    {
                        this.mDroneOffset.y = this.mDroneOffset.y + LowFrequencyThread.mrPreviousUpdateTimeStep * this.mrStateTimer;
                    }
                    else
                    {
                        this.mFlyState = BlueprintBuilder.eFlyState.eTravelling;
                        this.mrStateTimer = 0f;
                    }
                }
                if (this.mFlyState == BlueprintBuilder.eFlyState.eTravelling)
                {
                    this.mVTT.x = this.mTargetDroneOffset.x - this.mDroneOffset.x;
                    this.mVTT.z = this.mTargetDroneOffset.z - this.mDroneOffset.z;
                    this.mVTT.y = 0f;
                    if (this.mVTT.sqrMagnitude < 0.75f)
                    {
                        this.mFlyState = BlueprintBuilder.eFlyState.eLowering;
                    }
                    float num = 5f + this.mrStateTimer / 2f;
                    if (num > 10f)
                    {
                        num = 10f;
                    }
                    if (num > this.mVTT.magnitude)
                    {
                        num = this.mVTT.magnitude;
                    }
                    this.mVTT.Normalize();
                    this.mDroneOffset += this.mVTT * LowFrequencyThread.mrPreviousUpdateTimeStep * num;
                }
                if (this.mFlyState == BlueprintBuilder.eFlyState.eLowering)
                {
                    Vector3 zero = Vector3.zero;
                    zero.x = this.mTargetDroneOffset.x - this.mDroneOffset.x;
                    zero.z = this.mTargetDroneOffset.z - this.mDroneOffset.z;
                    this.mDroneOffset += zero * LowFrequencyThread.mrPreviousUpdateTimeStep;
                    float num2 = this.mDroneOffset.y - this.mTargetDroneOffset.y;
                    this.mDroneOffset.y = this.mDroneOffset.y - num2 * LowFrequencyThread.mrPreviousUpdateTimeStep;
                    if (num2 < 0.1f && WorldScript.mbIsServer)
                    {
                        this.SetFalcorState(BlueprintBuilder.eState.eBuildingBlock);
                    }
                }
                break;
            case BlueprintBuilder.eState.eBuildingBlock:
                if (this.mrStateTimer > 2f)
                {
                    
                    if (WorldScript.mbIsServer && this.mCarriedItem == null || this.GetControllingBuilder().CurrentBlueprint == null)
                    {
                        Debug.LogError("Error, how did BlueprintBuilder end up trying to build a block but we didn't have a carry item or with a null BP?");
                    }
                    if (!this.BuildBlock())
                        return;
                    this.SetFalcorState(BlueprintBuilder.eState.eReturning);
                    this.mTargetDroneOffset = Vector3.up;
                    this.mFlyState = BlueprintBuilder.eFlyState.eRising;
                    this.mVTT.x = this.mTargetDroneOffset.x - this.mDroneOffset.x;
                    this.mVTT.z = this.mTargetDroneOffset.z - this.mDroneOffset.z;
                    this.mVTT.y = 0f;
                    //if (this.mbLinkedToGO && !this.mbWellBehindPlayer && this.mCarriedItem != null)
                    //{
                    //    FloatingCombatTextManager.instance.QueueText(this.mVisualDroneLocation + Vector3.up, 1f, this.mCarriedItem.ToString(), Color.green, 1f, 64f);
                    //}
                    // Add sparking effect to UnityUpdate when in this state?  Check HEIST effect?  Or Pipe Extruder
                }
                break;
            case BlueprintBuilder.eState.eReturning:
                if (this.mFlyState == BlueprintBuilder.eFlyState.eRising)
                {
                    this.mVTT = this.mTargetDroneOffset - this.mDroneOffset;
                    if (this.mDroneOffset.y < this.mTargetDroneOffset.y + (float)this.mnOurRise)
                    {
                        this.mDroneOffset.y = this.mDroneOffset.y + LowFrequencyThread.mrPreviousUpdateTimeStep * this.mrStateTimer;
                    }
                    else
                    {
                        this.mFlyState = BlueprintBuilder.eFlyState.eTravelling;
                        this.mrStateTimer = 0f;
                    }
                }
                if (this.mFlyState == BlueprintBuilder.eFlyState.eTravelling)
                {
                    this.mVTT.x = this.mTargetDroneOffset.x - this.mDroneOffset.x;
                    this.mVTT.z = this.mTargetDroneOffset.z - this.mDroneOffset.z;
                    this.mVTT.y = 0f;
                    if (this.mVTT.sqrMagnitude < 0.75f)
                    {
                        this.mFlyState = BlueprintBuilder.eFlyState.eLowering;
                        this.mrStateTimer = 0f;
                    }
                    float num3 = 5f + this.mrStateTimer / 2f;
                    if (num3 > 10f)
                    {
                        num3 = 10f;
                    }
                    if (num3 > this.mVTT.magnitude)
                    {
                        num3 = this.mVTT.magnitude;
                    }
                    this.mVTT.Normalize();
                    this.mDroneOffset += this.mVTT * LowFrequencyThread.mrPreviousUpdateTimeStep * num3;
                }
                if (this.mFlyState == BlueprintBuilder.eFlyState.eLowering)
                {
                    this.mVTT = -this.Forwards;
                    this.mTargetDroneOffset.y = 0f;
                    Vector3 zero2 = Vector3.zero;
                    zero2.x = this.mTargetDroneOffset.x - this.mDroneOffset.x;
                    zero2.z = this.mTargetDroneOffset.z - this.mDroneOffset.z;
                    this.mDroneOffset += zero2 * LowFrequencyThread.mrPreviousUpdateTimeStep;
                    float num4 = this.mDroneOffset.y - this.mTargetDroneOffset.y;
                    this.mDroneOffset.y = this.mDroneOffset.y - num4 * LowFrequencyThread.mrPreviousUpdateTimeStep;
                    if (WorldScript.mbIsServer && num4 < 0.1f)
                    {
                        if (mCarriedItem != null)
                            this.SetFalcorState(BlueprintBuilder.eState.eLookingToOffloadCargo);
                        else
                            this.SetFalcorState(eState.eDocked);
                    }
                }
                break;
            case BlueprintBuilder.eState.eLookingToOffloadCargo:
                if (WorldScript.mbIsServer)
                {
                    if (!this.LocalStorage.IsFull())
                    {
                        ItemBase remain = this.LocalStorage.AddItem(this.mCarriedItem);
                        if (remain == null)
                        {
                            this.mCarriedItem = null;
                            this.SetFalcorState(BlueprintBuilder.eState.eDocked);
                        }
                    }
                }
                break;
        }
    }

    private void ChooseDestination()
    {
        //Determine which block you will build and set up plan to travel there
        BlueprintBuilder builder = this.GetControllingBuilder();
        if (builder.CurrentBlueprint == null)
        {
            Debug.LogWarning("BlueprintBuilder ChooseDestination when blueprint is null.  The build state should be changed to prevent this!");
            this.SetBuildState(BuildState.Idle);
            this.SetFalcorState(eState.eDocked);
            return;
        }
        if (this.mCarriedItem != null)
        {
            Debug.LogWarning("Blueprint builder is trying to choose destination when it already has an item? Unloading...");
            this.SetFalcorState(eState.eLookingToOffloadCargo);
        }
        Blueprint bp = builder.CurrentBlueprint;
        bool noworkleft = true;
        for (int y = 0; y < bp.SizeY; y++)
        {
            for (int x = 0; x < bp.SizeX; x++)
            {
                for (int z = 0; z < bp.SizeZ; z++)
                {
                    if (!builder.BlockPlaced[x, y, z] && !builder.CoordsLocked(x, y, z))
                    {
                        if (builder.InventoryHas(bp.Blocks[x, y, z], out this.mCarriedItem))
                        {
                            this.TargetIndex = new CubeCoord(x, y, z);
                            if (!builder.ApplyLock(this.TargetIndex))
                                this.SetFalcorState(eState.eLookingToOffloadCargo);
                            this.TargetCoord = builder.GetBPCubeCoordinates(x, y, z);
                            this.TargetPower = this.GetTargetPower(this.TargetCoord);
                            this.PowerProgress = this.TargetPower < this.PowerProgress ? this.TargetPower : this.PowerProgress;  // For load in case
                            this.SetFalcorState(eState.eCalculatingRoute);
                            return;
                        }
                        noworkleft = false;
                    }
                }
            }
        }
        if (noworkleft && builder.LockedCoords.Count == 0)
        {
            this.SetFalcorState(eState.eDocked);
            if (MissionManager.instance != null && this == builder)
                MissionManager.instance.AddMission("Blueprint " + bp.Name + " Finished!", 60.0f, Mission.ePriority.eImportant, false, false);
            this.SetBuildState(BuildState.Idle);
        }
    }

    /// <summary>
    /// Determines if the given blueprint block is available in the local inventory to build with
    /// </summary>
    /// <param name="block">Blueprint cube block in question</param>
    /// <param name="count">Refers to the amount of construction paste required for decorative blocks</param>
    /// <returns>True if the item is available</returns>
    public bool InventoryHas(CubeBlock block, out ItemBase item)
    {
        ushort type = block.Type;
        ushort val = block.Value;
        uint count = 1;
        item = null;

        // Convert blueprint multiblock into proper cube type/value
        if (type != eCubeTypes.MachinePlacementBlock)
        {
            //Handle conversion of mod blocks to local world cube types
            this.ModBlockTypeUpdate(ref type, ref val);
            //Convert multiblocks and decorations to their appropriate type 
            count = this.FinalBlockTypeUpdate(ref type, ref val);
        }

        foreach (ItemBase itemcheck in this.LocalStorage.Inventory)
        {
            ItemCubeStack stack = itemcheck as ItemCubeStack;
            if (stack != null)
            {
                if (stack.mCubeType == type && stack.mCubeValue == val && stack.mnAmount >= count)
                {
                    //Debug.Log("BlueprintBuilder Initial storage count: " + this.LocalStorage.ItemCount());
                    ItemBase itemtoget = itemcheck.NewInstance().SetAmount((int)count);
                    item = this.LocalStorage.RemoveItem(itemtoget);
                    //Debug.Log("BlueprintBuilder Removing item " + itemtoget.ToString() + " and got item " + item.ToString() + " inventory remaining: " + this.LocalStorage.ItemCount());
                    if (item != null)
                        return true;
                }
            }
        }
        //Debug.Log("Blueprint Builder failed to find item of type '" + type + "' in inventory. Current inventory count: " + this.LocalStorage.ItemCount());
        return false;
    }

    private void ModBlockTypeUpdate(ref ushort type, ref ushort val)
    {
        // Not a mod block
        if (type < 48897)
            return;

        int cubecheck = type << 16 | val;
        string modkey;
        if (this.CurrentBlueprint.ModBlocks.TryGetValue(cubecheck, out modkey))
        {
            string[] stringSeparators = new string[] { ".." };
            string[] modkeys = modkey.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries);

            ModCubeMap cubemap;
            if (ModManager.mModMappings.CubesByKey.TryGetValue(modkeys[0], out cubemap))
            {
                type = cubemap.CubeType;
                if (modkeys.Length > 1)
                {
                    ModCubeValueMap valmap;
                    if (cubemap.ValuesByKey.TryGetValue(modkeys[1], out valmap))
                        val = valmap.Value;
                    else
                        Debug.LogWarning("Blueprint builder found mod block cube type but failed to find the value entry '" + modkeys[1] + "'");
                }
            }
            else
                Debug.LogWarning("Blueprint builder found mod block with key '" + modkeys[0] + "' that is missing from modmappings.  User may be missing required mod for this blueprint!");
        }
    }

    private uint FinalBlockTypeUpdate(ref ushort type, ref ushort val)
    {
        uint count = 1;
        // Multiblocks need to pull the actual buildable component block
        TerrainDataEntry entry = TerrainData.mEntries[type];
        if (entry != null && entry.isMultiBlockMachine && !string.IsNullOrEmpty(entry.PickReplacement))
        {
            TerrainDataEntry replacement;
            TerrainDataValueEntry valueentry;
            TerrainData.GetCubeByKey(entry.PickReplacement, out replacement, out valueentry);
            if (replacement != null)
            {
                type = replacement.CubeType;
                val = valueentry.Value;
            }
            else
                Debug.LogWarning("Blueprint Builder found multiblock machine with pick replacement but the entry for it is missing!");
        }

        // Decorations are converted to their construction paste cost
        if (entry.Category == MaterialCategories.Decoration)
        {
            CraftData lData = CraftData.GetCraftDataForType(type, val);
            // Verify that it is craftable with paste and only paste
            if (lData != null && lData.Costs.Count == 1 && lData.Costs[0].CubeType == eCubeTypes.ConstructionPaste)
            {
                count = lData.Costs[0].Amount;
                type = eCubeTypes.ConstructionPaste;
                val = 0;
            }
        }
        return count;
    }


    private float GetTargetPower(CubeCoord coords)
    {
        return (float)(Math.Abs(coords.x - this.mnX) + Math.Abs(coords.y - this.mnY) + Math.Abs(coords.z - this.mnZ)) * PowerPerDistance + PowerPerMachine;
    }

    public bool ApplyLock(CubeCoord coords)
    {
        if (!this.LockedCoords.Contains(coords))
            this.LockedCoords.Add(coords);
        else
        {
            Debug.LogWarning("BlueprintBuilder attempted to build at locked site.  Race?");
            return false;
        }
        return true;
    }

    public void ReleaseLock(CubeCoord coords)
    {
        if (this.LockedCoords.Contains(coords))
            this.LockedCoords.Remove(coords);
    }

    public bool CoordsLocked(int x, int y, int z)
    {
        return this.LockedCoords.Exists(c => c.x == x && c.y == y && c.z == z);
    }

    private bool BuildBlock()
    {
        if (this.PowerProgress < this.TargetPower)
            return false;

        long lTestX = this.TargetCoord.x;
        long lTestY = this.TargetCoord.y;
        long lTestZ = this.TargetCoord.z;
        Segment segment;
        long segX;
        long segY;
        long segZ;
        WorldHelper.GetSegmentCoords(lTestX, lTestY, lTestZ, out segX, out segY, out segZ);
        segment = WorldScript.instance.mPlayerFrustrum.GetSegment(lTestX, lTestY, lTestZ);
        if (segment == null)
            segment = this.AttemptGetSegment(lTestX, lTestY, lTestZ);
        if (segment == null)
            segment = WorldScript.instance.GetSegment(segX, segY, segZ);
        if (segment == null)
            return false;

        ushort type;
        ushort val;
        byte flags;
        type = this.GetCube(lTestX, lTestY, lTestZ, out val, out flags);
        BlueprintBuilder builder = this.GetControllingBuilder();
        CubeBlock BPBlock = builder.CurrentBlueprint.Blocks[this.TargetIndex.x, this.TargetIndex.y, this.TargetIndex.z];
        byte newflags = this.GetNewCubeFlags((int)this.TargetIndex.x, (int)this.TargetIndex.y, (int)this.TargetIndex.z, builder);

        if (type == BPBlock.Type && val == BPBlock.Value && flags == newflags)
        {
            Debug.LogWarning("BlueprintBuilder was about to build a block but someone already built this block.  Did the player interfere? Returning home.");
            this.SetFalcorState(eState.eDocked);
        }
        if (this.BuildAllowance == BuildType.FillAirOnly && type != 1)
        {
            Debug.LogWarning("BlueprintBuilder was about to build a block but build type is fill air only and found non-air block!");
            this.SetBuildState(BuildState.Blocked);
            this.SetFalcorState(eState.eDocked);
            this.QueueExtraItem(this.mCarriedItem);
        }
        if (this.BuildAllowance == BuildType.ReplaceAll && TerrainData.GetHardness(type, val) >= 250)
        {
            Debug.LogWarning("BlueprintBuilder was about to build a block but found block with hardness >= 250.  Did a hivemind grow into the blueprint space?");
            this.SetBuildState(BuildState.Blocked);
            this.SetFalcorState(eState.eDocked);
            this.QueueExtraItem(this.mCarriedItem);
        }

        //Debug.Log("BlueprintBuilder building block at location: " + this.TargetCoord.ToString());
        ItemCubeStack stack = this.mCarriedItem as ItemCubeStack;
        if (stack.mCubeType != eCubeTypes.ConstructionPaste)
        {
            WorldScript.instance.BuildOrientationFromEntity(segment, lTestX, lTestY, lTestZ, stack.mCubeType, stack.mCubeValue, newflags);
            
            // This uses the player's frustrum... which may not include the multiblock... how do I find the Frustrum for location at x, y, z?
            if (stack.mCubeType == eCubeTypes.MachinePlacementBlock)
                StructureDetectionScript.instance.VoxelBuilt(WorldScript.instance.localPlayerInstance.mPlayerFrustrum, lTestX, lTestY, lTestZ, stack.mCubeType, stack.mCubeValue);
        }
        else  //Revert to exact BP block type for paste built ones
        {
            ushort t = BPBlock.Type;
            ushort v = BPBlock.Value;
            ModBlockTypeUpdate(ref t, ref v);
            WorldScript.instance.BuildOrientationFromEntity(segment, lTestX, lTestY, lTestZ, t, v, newflags);
        }
        this.mCarriedItem = null;
        builder.ReleaseLock(this.TargetIndex);
        builder.BlockPlaced[this.TargetIndex.x, this.TargetIndex.y, this.TargetIndex.z] = true;
        this.TargetIndex = CubeCoord.Invalid;
        this.TargetCoord = CubeCoord.Invalid;
        this.TargetPower = 0;
        this.PowerProgress = 0;
        builder.NumberPlaced++;
        builder.HoloDirty = true;
        builder.RequestImmediateNetworkUpdate();
        return true;
    }

    private void QueueExtraItem(ItemBase item)
    {
        BlueprintBuilder builder = this.GetControllingBuilder();
        lock (builder.ExtraLock)
        {
            FreightListing listing = builder.ExtraItems.Where(x => x.Item.Compare(item)).FirstOrDefault();
            if (listing != null)
                listing.Quantity++;
            else
                builder.ExtraItems.Add(new FreightListing(item.NewInstance(), 1));
        }
    }

    private void SetFalcorState(BlueprintBuilder.eState leNewState)
    {
        if (leNewState != this.mState)
        {
            this.RequestImmediateNetworkUpdate();
            this.MarkDirtyDelayed();
        }
        this.mState = leNewState;
        this.mrStateTimer = 0f;
        if (leNewState == BlueprintBuilder.eState.eCalculatingRoute)
        {
            this.mnOurRise = 2;
            this.mnTargetRise = 2;
            this.mFlyState = BlueprintBuilder.eFlyState.eParked;
        }
        if (leNewState == BlueprintBuilder.eState.eTravellingToBuildSite)
        {
            if (this.TargetCoord == CubeCoord.Invalid)
            {
                if (WorldScript.mbIsServer)
                {
                    this.mState = BlueprintBuilder.eState.eDocked;
                }
            }
            else
            {
                this.mTargetDroneOffset = new Vector3((float)(this.TargetCoord.x - this.mnX), (float)(1L + this.TargetCoord.y - this.mnY), (float)(this.TargetCoord.z - this.mnZ));
            }
            this.mFlyState = BlueprintBuilder.eFlyState.eRising;
        }
        if (leNewState == BlueprintBuilder.eState.eWaitingForItem)
        {
            this.mFlyState = BlueprintBuilder.eFlyState.eParked;
        }
        if (leNewState == BlueprintBuilder.eState.eLookingToOffloadCargo)
        {
            this.mFlyState = BlueprintBuilder.eFlyState.eParked;
        }
    }

    private void CalculateRoute()
    {
        if (this.mRequest == null)
        {
            this.mRequest = RaycastManager.instance.RequestRaycast(this.mnX, this.mnY + this.mnOurRise, this.mnZ, Vector3.zero, this.TargetCoord.x, this.TargetCoord.y + this.mnTargetRise, this.TargetCoord.z, Vector3.zero);
            this.mRequest.mbHitStartCube = false;
            return;
        }
        if (this.mRequest.mResult != null)
        {
            if (this.mRequest.mResult.mbHitSomething)
            {
                this.mnOurRise++;
                this.mnTargetRise++;
                this.mRequest = null;
                return;
            }
            this.SetFalcorState(BlueprintBuilder.eState.eTravellingToBuildSite);
            RaycastManager.instance.RenderDebugRay(this.mRequest, 0f, 0f);
            this.mRequest = null;
        }
    }
    #endregion

    #region BPConfig
    public void ConfiguringBlueprint()
    {
        // We'll let the server do all the work since it will transmit Falcor state stuff anyway
        if (!WorldScript.mbIsServer || this.ConnectedMachine == eConnectionType.Builder)
            return;
        if (this.CurrentBlueprint == null)
        {
            Debug.LogWarning("BlueprintBuilder was trying to configure blueprint for null blueprint!");
            this.SetBuildState(BuildState.Idle);
            return;
        }
        else if (this.CurrentBlueprint != null && this.CurrentBlueprint.Blocks == null)
        {
            // Loaded in Blueprint but only have name so far, check to see if they've finished loading in
            Blueprint bp;
            if (Blueprint.LoadedBlueprints.TryGetValue(this.CurrentBlueprint.Name, out bp))
            {
                this.CurrentBlueprint = bp;
                this.Hologram = new GameObject[bp.SizeX, bp.SizeY, bp.SizeZ];
            }
            else
                return;
        }

        int SizeX = this.CurrentBlueprint.SizeX;
        int SizeY = this.CurrentBlueprint.SizeY;
        int SizeZ = this.CurrentBlueprint.SizeZ;

        int blocklimit = 0;
        int blockcap = (SizeX * SizeY * SizeZ) / 15;  // Configuring locked into 3 seconds regardless of size

        this.BlockPlaced = new bool[SizeX, SizeY, SizeZ];
        CubeCoord coords = new CubeCoord();
        ushort type;
        ushort val;
        byte flags;
        byte newflags;

        try
        {
            for (int x = 0; x < SizeX; x++)
            {
                for (int y = 0; y < SizeY; y++)
                {
                    for (int z = 0; z < SizeZ; z++)
                    {
                        //Restore last state
                        if (x == 0 && y == 0 && z == 0)
                        {
                            x = xcBP;
                            y = ycBP;
                            z = zcBP;
                        }

                        // Save state if we've hit the cap
                        if (blocklimit > blockcap)
                        {
                            xcBP = x;
                            ycBP = y;
                            zcBP = z;
                            BlueprintBuilderWindow.dirty = true;  // Update total power cost per update ;)
                            return;
                        }
                        coords = this.GetBPCubeCoordinates(x, y, z);
                        type = this.GetCube(coords.x, coords.y, coords.z, out val, out flags);
                        newflags = this.GetNewCubeFlags(x, y, z, this);
                        this.BlockPlaced[x, y, z] = this.CheckBPCubeMatch(this.CurrentBlueprint.Blocks[x, y, z], type, val, flags, newflags);
                        //Debug.Log("BlueprintBuilder configured block at coords " + coords.ToString() + " and index " + x + ", " + y + ", " + z + " with type '" + this.CurrentBlueprint.Blocks[x,y,z].Type + "' to " + this.BlockPlaced[x, y, z].ToString());
                        this.BPPowerCost += (int)this.GetTargetPower(coords);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("BlueprintBuilder HandleConfigureBlueprint had an exception! " + e.Message);
            throw (e);
        }
        //Debug.Log("BlueprintBuilder Configure needed items list total: " + this.NeededItems.Count);
        this.RefreshNeededItems();
        //Debug.Log("BlueprintBuilder Configure needed items list total after refresh: " + this.NeededItems.Count + " Inventory count: " + this.LocalStorage.ItemCount());
        if (this.BuildFromSave)
        {
            this.SetBuildState(BuildState.Building);
            this.BuildFromSave = false; // Only on the first time ;)
        }
        else
            this.SetBuildState(BuildState.AwaitingStart);
    }

    /// <summary>
    /// Updates needed items list based on inventory (only run on the controlling builder)
    /// </summary>
    private void RefreshNeededItems()
    {
        List<FreightListing> extras = new List<FreightListing>();
        foreach (ItemBase item in this.LocalStorage.Inventory)
        {
            lock (ItemsLock)
            {
                FreightListing listing = this.NeededItems.FirstOrDefault(x => x.Item.Compare(item));
                if (listing != null)
                {
                    int amount = item.GetAmount();
                    int rem;
                    if (listing.Quantity > amount)
                        listing.Quantity -= amount;
                    else
                    {
                        rem = amount - listing.Quantity;
                        this.NeededItems.Remove(listing);
                        if (rem > 0)
                            extras.Add(new FreightListing(item, rem));
                    }
                }
                else
                {
                    extras.Add(new FreightListing(item, item.GetAmount()));
                }
            }
        }
        lock (ExtraLock)
        {
            this.ExtraItems = extras;
        }
    }

    /// <summary>
    /// Compares the world block against the blueprint block to determine if a block needs placing
    /// </summary>
    /// <param name="BPBlock">The blueprint block for the location</param>
    /// <param name="type">World block type</param>
    /// <param name="val">World block value</param>
    /// <param name="flags">World block flags</param>
    /// <param name="newflags">Blueprint block flags accounting for appropriate rotation of the blueprint</param>
    /// <returns></returns>
    public bool CheckBPCubeMatch(CubeBlock BPBlock, ushort type, ushort val, byte flags, byte newflags)
    {
        // 250 is T2 (451 is the Mk3 Build Gun and can dig T4 ores, 150 is mk1)
        if (this.BuildAllowance == BuildType.FillAirOnly && type != 1 || (type == BPBlock.Type && val == BPBlock.Value && flags == newflags) || (type == 1 && BPBlock.Type == 1))
        {
            if (type == BPBlock.Type && type != 1)
            {
                TotalBlocks++;
                NumberPlaced++;
            }
            return true;
        }
        else if (TerrainData.GetHardness(type, val) >= 250 || (this.BuildAllowance == BuildType.RequireAir && type != 1 && BPBlock.Type != 1))
        {
            this.SetBuildState(BuildState.Blocked);
            return false;
        }

        ushort t = BPBlock.Type;
        ushort v = BPBlock.Value;
        ModBlockTypeUpdate(ref t, ref v);
        uint amount = FinalBlockTypeUpdate(ref t, ref v);
        ItemBase item = ItemManager.SpawnCubeStack(t, v, (int)amount);
        bool match = false;
        lock (ItemsLock)
        {
            foreach (FreightListing listitem in this.NeededItems)
            {
                if (listitem.Item.Compare(item))
                {
                    listitem.Quantity++;
                    match = true;
                }
            }
            if (!match && t != 1)
                this.NeededItems.Add(new FreightListing(item, 1));
        }
        this.TotalBlocks++;
        return false;
    }

    public ushort GetCube(long lTestX, long lTestY, long lTestZ, out ushort lValue, out byte lFlags)
    {
        if (lTestX < 100000L)
            Debug.LogError(("Error, BlueprintBuilder failed GetCube Check! X is " + lTestX));
        if (lTestY < 100000L)
            Debug.LogError(("Error, BlueprintBuilder failed GetCube Check! Y is " + lTestY));
        if (lTestZ < 100000L)
            Debug.LogError(("Error, BlueprintBuilder failed GetCube Check! Z is " + lTestZ));
        Segment segment;
        long segX;
        long segY;
        long segZ;
        WorldHelper.GetSegmentCoords(lTestX, lTestY, lTestZ, out segX, out segY, out segZ);
        segment = WorldScript.instance.mPlayerFrustrum.GetSegment(lTestX, lTestY, lTestZ);
        if (segment == null)
            segment = this.AttemptGetSegment(lTestX, lTestY, lTestZ);
        if (segment == null)
            segment = WorldScript.instance.GetSegment(segX, segY, segZ);
        if (segment == null || !segment.mbInitialGenerationComplete || segment.mbDestroyed)
        {
            Debug.LogWarning("BlueprintBuilder GetCube could not find a segment for blueprint configuring.  Retry next update!");
            CentralPowerHub.mnMinecartX = lTestX;
            CentralPowerHub.mnMinecartY = lTestY;
            CentralPowerHub.mnMinecartZ = lTestZ;
            lFlags = (byte)0;
            lValue = (ushort)0;
            return 0;
        }
        lValue = segment.GetCubeData(lTestX, lTestY, lTestZ).mValue;
        lFlags = segment.GetCubeData(lTestX, lTestY, lTestZ).meFlags;
        return segment.GetCube(lTestX, lTestY, lTestZ);
    }

    /// <summary>
    /// Get the absolute coordinates of a Blueprint Cube from it's index in Blocks
    /// </summary>
    /// <param name="x">Block's x array index</param>
    /// <param name="y">Block's y array index</param>
    /// <param name="z">Block's z array index</param>
    /// <returns></returns>
    public CubeCoord GetBPCubeCoordinates(int x, int y, int z)
    {
        // Given the number of times this will be called for configuring the BP it may be worth caching the core parameters
        // Optimization can come later though... after it works :P

        //Taken straight from the Blueprint hologram build code....
        //------------------------------------------------------------------
        int SizeX = this.CurrentBlueprint.SizeX;
        int SizeY = this.CurrentBlueprint.SizeY;
        int SizeZ = this.CurrentBlueprint.SizeZ;

        // Allow changing build direction later maybe!
        long xcoord = this.mnX + (long)this.BuildDirection.x;
        long ycoord = this.mnY;
        long zcoord = this.mnZ + (long)this.BuildDirection.z;
        //SurvivalParticleManager.DebugRayCast(this.mnX, this.mnY, this.mnZ, xcoord, ycoord, zcoord, Color.blue);

        Vector3 BuildNormal = -this.Forwards;
        //SurvivalParticleManager.DebugRayCast(xcoord, ycoord, zcoord, xcoord + (long)BuildNormal.x, ycoord + (long)BuildNormal.y, zcoord + (long)BuildNormal.z, Color.green);

        // Get index incrementors based on orientation
        int xl = Math.Abs(this.BuildDirection.x) > 0.1 ? (int)this.BuildDirection.x : (int)BuildNormal.x;
        //int yl = 1;
        int zl = Math.Abs(this.BuildDirection.z) > 0.1 ? (int)this.BuildDirection.z : (int)BuildNormal.z;

        // Handle coordinate transforms for rotation and mirroring
        bool flipx = false, flipz = false;
        switch (this.BPRotation)
        {
            case RotState.Rotated90:
                flipz = true;
                break;
            case RotState.Rotated180:
                flipx = true;
                flipz = true;
                break;
            case RotState.Rotated270:
                flipx = true;
                break;
        }
        if (this.BPMirroredX)
            flipx = !flipx;
        if (this.BPMirroredZ)
            flipz = !flipz;

        //Compensate for orientation of the builder
        if (Vector3.Dot(this.Forwards, Vector3.back) > 0.8 || Vector3.Dot(this.Forwards, Vector3.right) > 0.8)
            flipx = !flipx;
        if (Vector3.Dot(this.Forwards, Vector3.forward) > 0.8 || Vector3.Dot(this.Forwards, Vector3.right) > 0.8)
            flipz = !flipz;

        int RotX;
        int RotZ;

        if (BPRotation == RotState.Original || BPRotation == RotState.Rotated180)
        {
            if (flipx)
                RotX = SizeX - x - 1;
            else
                RotX = x;
            if (flipz)
                RotZ = SizeZ - z - 1;
            else
                RotZ = z;
        }
        else
        {
            if (flipx)
                RotX = SizeZ - z - 1;
            else
                RotX = z;
            if (flipz)
                RotZ = SizeX - x - 1;
            else
                RotZ = x;
        }

        //if (flipx)
        //    RotX = LimitX - x - 1;
        //else
        //    RotX = x;
        //if (flipz)
        //    RotZ = LimitZ - z - 1;
        //else
        //    RotZ = z;
        //------------------------------------------------------------------

        //if (BPRotation == RotState.Original || BPRotation == RotState.Rotated180)
        //{
            xcoord += RotX * xl;
            ycoord += y;
            zcoord += RotZ * zl;
        //}
        //else
        //{
        //    xcoord += RotZ * xl;
        //    ycoord += y;
        //    zcoord += RotX * zl;
        //}

        return new CubeCoord(xcoord, ycoord, zcoord);
    }

    /// <summary>
    /// Returns the new cube flags based on all rotation/orientation parameters
    /// </summary>
    /// <param name="x">Block's x array index</param>
    /// <param name="y">Block's y array index</param>
    /// <param name="z">Block's z array index</param>
    /// <returns>The new cube flags</returns>
    public byte GetNewCubeFlags(int x, int y, int z, BlueprintBuilder builder)
    {
        if (builder.CurrentBlueprint == null || builder.CurrentBlueprint.Blocks == null)
        {
            Debug.LogWarning("BlueprintBuilder GetNewCubeFlags had a new blueprint or block set.");
            return new byte();
        }
        Byte newFlags = builder.CurrentBlueprint.Blocks[x, y, z].Flags;

        // This is not a standard cube rotation!! This will account for rotation of the orientation axis as well!
        if (builder.BPRotation != RotState.Original)
        {
            for (int n = 0; n < (int)builder.BPRotation; n++)
                newFlags = SegmentCustomRenderer.RotateCW(newFlags);
        }

        // Apply mirroring
        if (builder.BPMirroredZ)
            newFlags = SegmentCustomRenderer.FlipZ(newFlags);
        if (builder.BPMirroredX)
            newFlags = SegmentCustomRenderer.FlipX(newFlags);

        return newFlags;
    }
    #endregion

    #region InterfaceFunctions
    public BlueprintBuilder GetControllingBuilder()
    {
        BlueprintBuilder builder = this;
        int id10tcheck = 0;
        //Passthrough to the controlling builder
        while (builder.ConnectedMachine == BlueprintBuilder.eConnectionType.Builder && builder.AttachedBuilder != null)
        {
            builder = builder.AttachedBuilder;
            id10tcheck++;
            if (id10tcheck > 100 || builder == this) return null; // Yeah, someone might build a closed loop >.>
        }
        return builder;
    }

    public void SetBlueprint(Blueprint blueprint)
    {
        if (this.AttachedBuilder == null)
        {
            this.CurrentBlueprint = blueprint;
            this.SetBuildState(BuildState.ConfiguringBlueprint);
            this.ClearHologram();
            this.Hologram = new GameObject[blueprint.SizeX, blueprint.SizeY, blueprint.SizeZ];
            this.LockedCoords = new List<CubeCoord>();
            this.SetFalcorState(eState.eDocked);
            this.BuildFromSave = false;
        }
        else
            Debug.LogWarning("BlueprintBuilder was asked to set it's blueprint when it isn't the controlling builder!");
    }

    public void StartBuilding()
    {
        this.SetBuildState(BuildState.Building);
    }

    public string CurrentBPName
    {
        get
        {
            if (this.GetControllingBuilder().CurrentBlueprint == null)
                return "No Blueprint";
            else if (string.IsNullOrEmpty(this.GetControllingBuilder().CurrentBlueprint.Name))
                return "Unnamed Blueprint";
            else
                return this.GetControllingBuilder().CurrentBlueprint.Name;
        }
    }

    private void SetBuildState(BuildState newstate)
    {
        if (newstate == BuildState.ConfiguringBlueprint)
        {
            if (this.Hologram != null)
                this.ClearHologram();
            this.HoloBuilding = true;
            this.xHG = 0;
            this.yHG = 0;
            this.zHG = 0;
            this.xcBP = 0;
            this.ycBP = 0;
            this.zcBP = 0;
            this.NeededItems = new List<FreightListing>();
            this.BPPowerCost = 0;
            this.TotalBlocks = 0;
            this.NumberPlaced = 0;
        }
        this.BuildMode = newstate;
        this.MarkDirtyDelayed();
        BlueprintBuilderWindow.networkredraw = true;
        this.RequestImmediateNetworkUpdate();
    }
    #endregion

    #region Unity_Methods
    public override void UnityUpdate()
    {
        if (!this.mbLinkedToGO)
        {
            if (this.mWrapper == null || !this.mWrapper.mbHasGameObject)
                return;
            if (this.mWrapper.mGameObjectList == null)
                Debug.LogError((object)"RA missing game object #0?");
            if (this.mWrapper.mGameObjectList[0].gameObject == null)
                Debug.LogError((object)"RA missing game object #0 (GO)?");
            this.DroneObject = this.mWrapper.mGameObjectList[0].gameObject.transform.Search("FALCORDRONE").gameObject;
            this.EngineObject = this.DroneObject.transform.Search("Auto Upgrader Drone Jets").gameObject;
            this.ThrustObject = this.DroneObject.transform.Search("_ExhaustMesh").gameObject;
            this.ThrustAudio = this.EngineObject.transform.Search("Audio").GetComponent<AudioSource>();
            this.WorkLight = this.mWrapper.mGameObjectList[0].gameObject.transform.Search("Upgrader Light").GetComponent<Light>();

            // Mining Sparks for build effect
            GameObject sparksobject = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.OreExtractor].transform.Search("MiningSparks").gameObject;
            if (sparksobject == null)
            {
                Debug.LogWarning("Failing to find mining sparks object for blueprint builder...");
                return;
            }
            ParticleSystem miningsparks = sparksobject.GetComponent<ParticleSystem>();
            this.Sparks = GameObject.Instantiate(miningsparks, this.DroneObject.transform);
            this.Sparks.Stop();
            ParticleSystem.MainModule mod = this.Sparks.main;
            mod.loop = false;
            mod.playOnAwake = true;
            this.Sparks.Clear();
            this.Sparks.Stop();
            this.Sparks.gameObject.SetActive(false);

            this.mVisDroneOffset = new Vector3(0.0f, 5f, 0.0f);
            if (HologramCubes.HoloMaterial == null)
            {
                HologramCubes.HoloMaterial = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.CentralPowerHub_MB].transform.Search("ConveyorBelt").gameObject.GetComponent<Renderer>().material;
                //this.HoloMaterial.mainTexture = null;
                //this.HoloMaterial.mainTexture = Resources.Load("Asset Packs/sci-fi_cockpit-07/Textures/glass2") as Texture;
                //this.HoloMaterial.SetColor("_EmissionColor", Color.gray);
                //DynamicGI.SetEmissive(GetComponent<Renderer>(), finalValue);
            }
            if (HologramCubes.HoloCube == null)
                HologramCubes.HoloCube = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.MassStorageOutputPort].transform.Search("HoloCube").gameObject;

            //GameObject testpipegraphics = (GameObject)GameObject.Instantiate(SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)SpawnableObjectEnum.PipeExtruder_T1]);
            //UnityEngine.Object[] objs = testpipegraphics.GetComponentsInChildren<UnityEngine.Object>();
            //foreach (UnityEngine.Object ob in objs)
            //{
            //    Debug.Log("BlueprintBuilder testgraphics child object name: " + ob.name + " with type: " + ob.GetType().ToString());
            //}

            this.mbLinkedToGO = true;
        }
        this.HandleDroneGraphics();
        if (this.HoloBuilding)
            this.HologramBuild();
        else if (this.HoloDirty)
            this.HandleHologramUpdate();

    }
    private void HandleDroneGraphics()
    {
        this.mVisDroneOffset += (this.mDroneOffset - this.mVisDroneOffset) * Time.deltaTime * 2.5f;
        this.mVisualDroneLocation = this.mWrapper.mGameObjectList[0].gameObject.transform.position + this.mVisDroneOffset;
        this.DroneObject.transform.position = this.mVisualDroneLocation;

        Vector3 vector3_1 = -this.mVTT;
        if (this.mFlyState == BlueprintBuilder.eFlyState.eTravelling)
            vector3_1 += new Vector3(0.0f, 0.75f, 0.0f);
        this.DroneObject.transform.forward += (vector3_1 - this.DroneObject.transform.forward) * Time.deltaTime * 1f;
        Vector3 vector3_2;
        if (this.mFlyState == BlueprintBuilder.eFlyState.eTravelling)
        {
            vector3_2 = this.DroneObject.transform.forward;
            vector3_2.y = 0.1f;
        }
        else
            vector3_2 = Vector3.down + this.DroneObject.transform.forward * 0.1f;
        this.EngineObject.transform.forward += (vector3_2 - this.EngineObject.transform.forward) * Time.deltaTime * 0.5f;
        if (this.mState == BlueprintBuilder.eState.eTravellingToBuildSite || this.mState == BlueprintBuilder.eState.eReturning)
        {
            this.ThrustObject.SetActive(true);
            if (this.mFlyState == BlueprintBuilder.eFlyState.eLowering)
            {
                if (this.ThrustAudio.pitch > 0.25)
                    this.ThrustAudio.pitch -= Time.deltaTime;
            }
            else if (this.ThrustAudio.pitch < 3.0)
                this.ThrustAudio.pitch += Time.deltaTime;
            this.ThrustAudio.volume = this.ThrustAudio.pitch / 3f;
            this.ReadyToDock = true;
        }
        else if (this.ThrustObject.activeSelf)
        {
            // Trigger audio only once
            if (this.ReadyToDock)
            {
                AudioHUDManager.instance.PlayMachineAudio(AudioHUDManager.instance.DroneDock, 1f, 1f, this.mVisualDroneLocation, 16f);
                this.ReadyToDock = false;
            }
            if (this.mState != eState.eBuildingBlock || this.mState == eState.eDocked) // Maintain hover!
            {
                this.ThrustObject.SetActive(false);
            }
        }
        if (this.mState == BlueprintBuilder.eState.eReturning && this.mFlyState == BlueprintBuilder.eFlyState.eLowering)
            this.WorkLight.enabled = true;
        else if (this.mState == BlueprintBuilder.eState.eTravellingToBuildSite && this.mFlyState == BlueprintBuilder.eFlyState.eRising)
            this.WorkLight.enabled = true;
        else
            this.WorkLight.enabled = false;
        if (this.mState == eState.eBuildingBlock)
        {
            //if (this.Sparks.isPlaying)
            //    Debug.Log("Sparks is currently playing! And object active status is " + this.Sparks.gameObject.activeSelf.ToString() + " Sparks time: " + this.Sparks.time.ToString());
            
            if (!this.Sparks.isPlaying)
            {
                Vector3 lPos = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(0, 0.5f);
                Sparks.transform.position = this.mVisualDroneLocation + new Vector3(0f, -0.75f, 0f) + lPos;
                //Debug.Log("BlueprintBuilder sparks position: " + Sparks.transform.position.ToString());
                this.Sparks.gameObject.SetActive(true);
                //if (!this.Sparks.isPlaying)
                //{
                //    //Debug.Log("Sparks was set active but still isn't playing!  Force it!");
                //    this.Sparks.Play();
                //}
            }
        }
        if (this.mState == eState.eReturning && this.Sparks.IsAlive())
        {
            this.Sparks.Clear();
            this.Sparks.Stop();
            this.Sparks.gameObject.SetActive(false);
        }
    }

    private void HologramBuild()
    {
        if (this.CurrentBlueprint == null || this.CurrentBlueprint.Blocks == null)
        {
            //On the client this may come up due to delays in the tranmission of the BP don't spam log for that case
            if (WorldScript.mbIsServer)
                Debug.LogWarning("Blueprint Builder Hologram build failed because no available blueprint or block data!");
            return;
        }

        int SizeX = this.CurrentBlueprint.SizeX;
        int SizeY = this.CurrentBlueprint.SizeY;
        int SizeZ = this.CurrentBlueprint.SizeZ;

        //Client doesn't get this created at configuration time among other poor handling... whoops!
        if (this.Hologram == null)
            this.Hologram = new GameObject[SizeX, SizeY, SizeZ];
        
        // Allow changing build direction later maybe!
        long xcoord = this.mnX + (long)this.BuildDirection.x;
        long ycoord = this.mnY;
        long zcoord = this.mnZ + (long)this.BuildDirection.z;
        SurvivalParticleManager.DebugRayCast(this.mnX, this.mnY, this.mnZ, xcoord, ycoord, zcoord, Color.blue);

        Vector3 BuildNormal = -this.Forwards;
        SurvivalParticleManager.DebugRayCast(xcoord, ycoord, zcoord, xcoord + (long)BuildNormal.x, ycoord + (long)BuildNormal.y, zcoord + (long)BuildNormal.z, Color.green);

        // Get index incrementors based on orientation
        int xl = Math.Abs(this.BuildDirection.x) > 0.1 ? (int)this.BuildDirection.x : (int)BuildNormal.x;
        int yl = 1;
        int zl = Math.Abs(this.BuildDirection.z) > 0.1 ? (int)this.BuildDirection.z : (int)BuildNormal.z;

        // Handle coordinate transforms for rotation and mirroring
        bool flipx = false, flipz = false;
        switch (this.BPRotation)
        {
            case RotState.Rotated90:
                flipz = true;
                break;
            case RotState.Rotated180:
                flipx = true;
                flipz = true;
                break;
            case RotState.Rotated270:
                flipx = true;
                break;
        }
        if (this.BPMirroredX)
            flipx = !flipx;
        if (this.BPMirroredZ)
            flipz = !flipz;

        //Compensate for orientation of the builder
        if (Vector3.Dot(this.Forwards, Vector3.back) > 0.8 || Vector3.Dot(this.Forwards, Vector3.right) > 0.8)
            flipx = !flipx;
        if (Vector3.Dot(this.Forwards, Vector3.forward) > 0.8 || Vector3.Dot(this.Forwards, Vector3.right) > 0.8)
            flipz = !flipz;

        //Debug.Log("Just to be sure Blueprint builder xl: " + xl + ", yl: " + yl + ", zl: " + zl);
        //Debug.Log("Forwards x: " + this.Forwards.x + ", y: " + this.Forwards.y + ", z: " + this.Forwards.z);
        //Debug.Log("Build direction x: " + BuildDirection.x + ", y: " + this.BuildDirection.y + ", z: " + this.BuildDirection.z);
        //Debug.Log("Build normal x: " + BuildNormal.x + ", y: " + BuildNormal.y + ", z: " + BuildNormal.z);

        CubeBlock block;
        ushort type;
        ushort val;
        byte flags;
        Vector3 pos;
        Quaternion rot;
        int FrameLimiter = 0;
        int RotX, RotZ;
        int LimitX, LimitZ;

        //Pick up where we left off on last frame
        xcoord += xl * xHG;
        ycoord += yl * yHG;
        zcoord += zl * zHG;
        //Debug.Log(" Hologram debug " + xHG + " " + yHG + " " + zHG);
        //Debug.Log(" Hologram debug " + xcoord + " " + ycoord + " " + zcoord);
        //Debug.Log(" Hologram debug " + xl + " " + yl + " " + zl);
        //Debug.Log(" Hologram debug " + SizeX + " " + SizeY + " " + SizeZ);

        if (BPRotation == RotState.Original || BPRotation == RotState.Rotated180)
        {
            LimitX = SizeX;
            LimitZ = SizeZ;
        }
        else
        {
            LimitX = SizeZ;
            LimitZ = SizeX;
        }

        try
        {
            for (int x = 0; x < LimitX; x++)
            {
                for (int y = 0; y < SizeY; y++)
                {
                    for (int z = 0; z < LimitZ; z++)
                    {
                        //Restore last state
                        if (x == 0 && y == 0 && z == 0)
                        {
                            x = xHG;
                            y = yHG;
                            z = zHG;
                        }

                        // Save state if we've hit the single-frame render limit
                        if (FrameLimiter > HoloFrameCap)
                        {
                            xHG = x;
                            yHG = y;
                            zHG = z;
                            //Debug.Log(" Hologram debug " + xHG + " " + yHG + " " + zHG);
                            //Debug.Log(" Hologram debug " + xcoord + " " + ycoord + " " + zcoord);
                            //Debug.Log(" Hologram debug " + xl + " " + yl + " " + zl);
                            return;
                        }

                        //Perform the actual flip of indexing
                        if (flipx)
                            RotX = LimitX - x - 1;
                        else
                            RotX = x;
                        if (flipz)
                            RotZ = LimitZ - z - 1;
                        else
                            RotZ = z;

                        //Debug.Log("Blueprint builder rotate state: " + BPRotation.ToString() + " RotX: " + RotX + " RotZ: " + RotZ);
                        //Referencing in the blueprint has to flip for 90/270 degree rotation
                        if (BPRotation == RotState.Original || BPRotation == RotState.Rotated180)
                            block = this.CurrentBlueprint.Blocks[RotX, y, RotZ];
                        else
                            block = this.CurrentBlueprint.Blocks[RotZ, y, RotX];

                        //Get the cube data and generate the hologram object
                        type = block.Type;
                        flags = block.Flags;
                        val = block.Value;
                        if (type != 1) //Short circuit checks for air
                        {
                            pos = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(xcoord, ycoord, zcoord);
                            rot = SegmentCustomRenderer.GetRotationQuaternion(flags);
                            rot = this.UpdateRotation(rot);
                            if (BPRotation == RotState.Original || BPRotation == RotState.Rotated180)
                                this.Hologram[RotX, y, RotZ] = HologramCubes.GetHologramFromCube(type, val, pos, rot);
                            else
                                this.Hologram[RotZ, y, RotX] = HologramCubes.GetHologramFromCube(type, val, pos, rot);
                        }
                        FrameLimiter++;  // Count air to the frame limit
                        zcoord += zl;
                    }
                    zcoord = this.mnZ + (long)this.BuildDirection.z;
                    ycoord += yl;
                }
                ycoord = this.mnY;
                xcoord += xl;
            }
        }
        catch (IndexOutOfRangeException)
        {
            Debug.LogWarning("BlueprintBuilder Hologram Build index out of range.  Rotation state changed from another thread?");
            //Rebuild!!
            this.ClearHologram();
            return;
        }
        this.HoloBuilding = false;
        this.HoloDirty = true;
        xHG = 0;
        yHG = 0;
        zHG = 0;
    }

    public Quaternion UpdateRotation(Quaternion rot)
    {
        // Apply rotation
        if (this.BPRotation != RotState.Original)
            rot *= Quaternion.Euler(Vector3.up * ((int)this.BPRotation * 90));

        // Apply mirroring
        if (this.BPMirroredZ)
        {
            //Need to turn off axis for Z mirroring due to absolute reference for cube orientation
            rot *= Quaternion.Euler(Vector3.up * 90);
            rot.Set(rot.x, -rot.y, -rot.z, rot.w);
            rot *= Quaternion.Euler(Vector3.up * 270);
        }
        if (this.BPMirroredX)
            rot.Set(-rot.x, -rot.y, rot.z, rot.w);
        return rot;
    }

    public void HandleHologramUpdate()
    {
        if (this.Hologram == null || this.CurrentBlueprint == null || this.BlockPlaced == null)
        {
            Debug.LogWarning("BlueprintBuilder attempted to update hologram that was null!");
            return;
        }

        int SizeX = this.CurrentBlueprint.SizeX;
        int SizeY = this.CurrentBlueprint.SizeY;
        int SizeZ = this.CurrentBlueprint.SizeZ;

        try
        {
            for (int x = 0; x < SizeX; x++)
            {
                for (int y = 0; y < SizeY; y++)
                {
                    for (int z = 0; z < SizeZ; z++)
                    {
                        if (this.BlockPlaced[x, y, z] && this.Hologram[x, y, z] != null)
                        {
                            //Check for multiblock and completion
                            CubeBlock BPBlock = this.CurrentBlueprint.Blocks[x, y, z];
                            CubeCoord coords = this.GetBPCubeCoordinates(x, y, z);
                            bool ignoredeletion = false;
                            if (BPBlock.Value == 1) //Possible multi-block center
                            {
                                TerrainDataEntry entry = TerrainData.mEntries[BPBlock.Type];
                                ushort valtest;
                                byte flags;
                                if (entry != null && entry.isMultiBlockMachine) // Verified as multiblock center
                                {
                                    //Ignore deleting the multiblock hologram until it has been fully constructed
                                    if (this.GetCube(coords.x, coords.y, coords.z, out valtest, out flags) == eCubeTypes.MachinePlacementBlock)
                                        ignoredeletion = true;
                                }
                            }
                            if (!ignoredeletion)
                            {
                                //Debug.Log("BlueprintBuilder Destroying hologram at index: " + x + ", " + y + ", " + z + " with coords: " + coords.ToString());
                                UnityEngine.Object.Destroy(this.Hologram[x, y, z]);
                                this.Hologram[x, y, z] = null;
                            }
                        }
                    }
                }
            }
        }
        catch (IndexOutOfRangeException e)
        {
            Debug.LogError("Hologram update went out of range?!  Where is this not accounted for!? Exception message: " + e.Message + " and stack trace: " + e.StackTrace);
            //Rebuild!!
            this.ClearHologram();
            this.Hologram = new GameObject[SizeX, SizeY, SizeZ];
            this.HoloBuilding = true;
            return;
        }
        this.HoloDirty = false;
    }

    public override string GetPopupText()
    {

        string s1 = "Blueprint Builder\nPower: " + CurrentPower.ToString("N0") + " / " + MaxPower.ToString("N0") + "\n";
        string s2 = "";
        string s3 = "";
        string s4 = "";
        BlueprintBuilder builder = this.GetControllingBuilder();
        if (builder.CurrentBlueprint == null)
            s2 = "Press (E) to configure\n";
        else
            s2 = "Current Blueprint: " + builder.CurrentBlueprint.Name + "\n";

        if (builder.BuildMode == BuildState.Building)
        {
            s3 = "Current block progress: " + this.PowerProgress.ToString("N0") + " / " + this.TargetPower.ToString("N0") + "\n";
            int percentcomplete = 100 * builder.NumberPlaced / builder.TotalBlocks;
            s3 += builder.NumberPlaced + " / " + builder.TotalBlocks + " blocks placed (" + percentcomplete + "%) \n";
        }

        ItemBase item = null;
        lock(builder.ItemsLock)
        {
            if (builder.NeededItems != null && builder.NeededItems.Count > 0)
            {
                FreightListing listitem = builder.NeededItems[0];
                if (listitem != null && listitem.Item != null)
                {
                    item = listitem.Item;
                    item.SetAmount(listitem.Quantity);
                }
            }
        }
        if (item != null)
            s4 = "Top requested item: " + item.ToString() + "\n";
        // Debug stuff
        //s3 = "Build State: " + builder.BuildMode.ToString() + "\nFalcor State: " + mState.ToString() + "\nFly state: " + mFlyState.ToString() + "\nDrone offset: " + this.mDroneOffset.ToString() + "\n" ;

        return s1 + s2 + s3 + s4;
    }

    public override void DropGameObject()
    {
        mbLinkedToGO = false;
        base.DropGameObject();
    }

    public void ClearHologram()
    {
        if (this.Hologram != null)
        {
            foreach (GameObject ob in this.Hologram)
            {
                if (ob != null)
                    UnityEngine.Object.Destroy(ob);
            }
            this.Hologram = null;
        }
    }

    public override void UnitySuspended()
    {
        this.ClearHologram();
        base.UnitySuspended();
    }

    public override void OnUpdateRotation(byte newFlags)
    {
        // Don't allow rotation while building or configuring otherwise rotate as normal
        if (this.BuildMode != BuildState.Idle)
        {
            int x = (int)(this.mnX - mSegment.baseX);
            int y = (int)(this.mnY - mSegment.baseY);
            int z = (int)(this.mnZ - mSegment.baseZ);

            // nodeworker automatically sets the new flags in the cubedata that is actually used by the diskserializer, so we have to restore them!
            mSegment.maCubeData[(y << 8) + (z << 4) + x].meFlags = this.mFlags;
        }
        else
        {
            this.Forwards = SegmentCustomRenderer.GetRotationQuaternion(newFlags) * Vector3.forward;
            this.Forwards.Normalize();
            this.BuildDirection = SegmentCustomRenderer.GetRotationQuaternion(newFlags) * Vector3.right;
            this.BuildDirection.Normalize();
            base.OnUpdateRotation(newFlags);
        }
    }

    public override void OnDelete()
    {
        this.LocalStorage.DropOnDelete();
        if (this.Hologram != null)
        {
            foreach (GameObject ob in this.Hologram)
            {
                if (ob != null)
                    UnityEngine.Object.Destroy(ob);
            }
            this.Hologram = null;
        }
        base.OnDelete();
    }
    #endregion

    #region Freight
    public List<FreightListing> FreightOfferings
    {
        get
        {
            lock (ExtraLock) { return ExtraItems; }
        }
    }

    public List<FreightListing> FreightRequests
    {
        get
        {
            this.FreightTimer = 5f; // If this is called it means a Freight station has locked onto us
            lock (ItemsLock) { return NeededItems; }
        }
    }

    public bool ProvideFreight(ItemBase item)
    {
        lock (ExtraLock)
        {
            FreightListing freight = this.ExtraItems.FirstOrDefault(x => x.Item.Compare(item));
            if (freight != null)
            {
                if (item.GetAmount() != 1)
                    item.SetAmount(1);
                ItemBase output = this.LocalStorage.RemoveItem(item);
                if (output != null)
                {
                    if (freight.Quantity > 1)
                        freight.Quantity--;
                    else
                        this.ExtraItems.Remove(freight);
                }
                return output != null;
            }
        }
        return false;
    }

    public bool ReceiveFreight(ItemBase item)
    {
        ItemBase extra;
        lock (ItemsLock)
        {
            if (this.NeededItems == null)
                return false;
            FreightListing freight = this.NeededItems.FirstOrDefault(x => x.Item.Compare(item));
            // Keep space to spare for returning items!
            if (freight != null && (this.LocalStorage.Inventory.GetItemCount() < (LocalMAXStorage - 15)))
            {
                extra = this.LocalStorage.AddItem(item.NewInstance());
                if (extra != null) return false;
                item = null;  // To be sure the cart doesn't do more with it than it should!
                if (freight.Quantity > 1)
                    freight.Quantity--;
                else
                {
                    this.NeededItems.Remove(freight);
                }
                return true;
            }
        }
        return false;
    }
    #endregion

    #region powerinterface
    public float GetMaxPower()
    {
        return MaxPower;
    }

    public float GetRemainingPowerCapacity()
    {
        return this.GetMaxPower() - this.CurrentPower;
    }

    public float GetMaximumDeliveryRate()
    {
        return MaxDelivery;
    }

    public bool DeliverPower(float amount)
    {
        if (amount <= this.GetRemainingPowerCapacity())
        {
            this.CurrentPower += amount;
            return true;
        }
        return false;
    }

    public bool WantsPowerFromEntity(SegmentEntity entity)
    {
        return true;
    }
    #endregion

    #region Serialization
    public override bool ShouldSave()
    {
        return true;
    }

    public override int GetVersion()
    {
        return 0;
    }

    public override bool ShouldNetworkUpdate()
    {
        return true;
    }

    public override void WriteNetworkUpdate(BinaryWriter writer)
    {
        writer.Write(this.CurrentPower);
        writer.Write(this.PowerProgress);
        writer.Write(this.TargetPower);
        writer.Write(this.NumberPlaced);
        writer.Write(this.TotalBlocks);

        if (this.CurrentBlueprint != null)
            writer.Write(this.CurrentBlueprint.Name);
        else
            writer.Write(string.Empty);
        writer.Write((byte)this.BuildMode);
        writer.Write((byte)this.BuildAllowance);

        writer.Write((byte)this.mState);
        writer.Write(mTargetDroneOffset.x);
        writer.Write(mTargetDroneOffset.y);
        writer.Write(mTargetDroneOffset.z);

        writer.Write((byte)mnOurRise);
        writer.Write((byte)mnTargetRise);
    }

    public override void ReadNetworkUpdate(BinaryReader reader)
    {
        this.CurrentPower = reader.ReadSingle();
        this.PowerProgress = reader.ReadSingle();
        this.TargetPower = reader.ReadSingle();
        this.NumberPlaced = reader.ReadInt32();
        this.TotalBlocks = reader.ReadInt32();

        string bpname = reader.ReadString();
        if (!string.IsNullOrEmpty(bpname) && (this.CurrentBlueprint == null || this.CurrentBlueprint.Name != bpname))
        {
            Blueprint bp;
            if (Blueprint.LoadedBlueprints.TryGetValue(bpname, out bp) && bp.Blocks != null)
                this.CurrentBlueprint = bp;
            else
            {
                //Debug.Log("Client requesting blueprint in BlueprintBuilder network update");
                ModManager.ModSendClientCommToServer("steveman0.RequestBlueprint", bpname);
                bp = new Blueprint(bpname);
                this.CurrentBlueprint = bp;  // Client should only ever be checking for the name or wait on receiving the actual BP
            }
        }
        this.BuildMode = (BuildState)reader.ReadByte();
        if (this.BuildMode == BuildState.ConfiguringBlueprint || this.BuildMode == BuildState.Building && this.Hologram == null && this.CurrentBlueprint != null && !this.HoloBuilding)
            this.SetBlueprint(this.CurrentBlueprint);
        else if (this.BuildMode == BuildState.Building)
            this.HoloDirty = true;
        this.BuildAllowance = (BuildType)reader.ReadByte();

        this.mState = (BlueprintBuilder.eState)reader.ReadByte();
        mTargetDroneOffset.x = reader.ReadSingle();
        mTargetDroneOffset.y = reader.ReadSingle();
        mTargetDroneOffset.z = reader.ReadSingle();

        mnOurRise = reader.ReadByte();
        mnTargetRise = reader.ReadByte();
        BlueprintBuilderWindow.dirty = true;
    }

    public override void Write(BinaryWriter writer)
    {
        this.LocalStorage.WriteInventory(writer);
        ItemFile.SerialiseItem(mCarriedItem, writer);
        writer.Write(this.CurrentPower);
        writer.Write(this.PowerProgress);

        if (this.CurrentBlueprint != null)
        {
            writer.Write(this.CurrentBlueprint.Name);
            writer.Write((byte)this.BPRotation);
            writer.Write(this.BPMirroredX);
            writer.Write(this.BPMirroredZ);
            writer.Write((byte)BuildAllowance);
            writer.Write(this.BuildMode == BuildState.Building);
        }
        else
            writer.Write(string.Empty);

        base.Write(writer);
    }

    public override void Read(BinaryReader reader, int entityVersion)
    {
        this.LocalStorage.ReadInventory(reader);
        this.mCarriedItem = ItemFile.DeserialiseItem(reader);
        this.CurrentPower = reader.ReadSingle();
        this.PowerProgress = reader.ReadSingle();

        string bpname = reader.ReadString();
        if (!string.IsNullOrEmpty(bpname))
        {
            this.BPRotation = (BlueprintBuilder.RotState)reader.ReadByte();
            this.BPMirroredX = reader.ReadBoolean();
            this.BPMirroredZ = reader.ReadBoolean();
            this.BuildAllowance = (BlueprintBuilder.BuildType)reader.ReadByte();
            this.BuildFromSave = reader.ReadBoolean();

            Blueprint bp;
            if (Blueprint.LoadedBlueprints.TryGetValue(bpname, out bp))
                this.CurrentBlueprint = bp;
            else
                this.CurrentBlueprint = new Blueprint(bpname);
            this.LockedCoords = new List<CubeCoord>();
            this.SetBuildState(BuildState.ConfiguringBlueprint);
        }

        // Offload the carried item and let it return to choose destination after the blueprint has configured
        if (this.mCarriedItem != null)
            this.SetFalcorState(eState.eLookingToOffloadCargo);
        else
            this.SetFalcorState(eState.eDocked);
    }
    #endregion

    #region enums
    public enum BuildState
    {
        Idle,
        ConfiguringBlueprint,
        Blocked,
        AwaitingStart,
        Building,
        Unknown,
    }

    public enum RotState
    {
        Original,
        Rotated90,
        Rotated180,
        Rotated270,
    }

    public enum eConnectionType
    {
        Hopper,
        Freight,
        Builder,
        None,
    }

    public enum BuildType
    {
        RequireAir,
        FillAirOnly,
        ReplaceAll
    }

    public enum eState
    {
        eDocked,
        eWaitingForItem,
        eCalculatingRoute,
        eTravellingToBuildSite,
        eBuildingBlock,
        eReturning,
        eLookingToOffloadCargo,
    }

    private enum eFlyState
    {
        eParked,
        eRising,
        eTravelling,
        eLowering,
    }
    #endregion
}

