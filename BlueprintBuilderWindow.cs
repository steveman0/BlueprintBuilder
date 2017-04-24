using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using MadVandal.FCModCore;

public class BlueprintBuilderWindow : BaseMachineWindow
{
    public static bool dirty;
    public static bool networkredraw;
    private WindowType CurrentWindow;
    public UIDropDownList<Blueprint> BlueprintList;

    public const string InterfaceName = "steveman0.InterfaceBlueprintBuilder";
    private const string InterfaceSelectBlueprint = "SelectBlueprint";
    private const string InterfaceRotateBlueprint = "RotateBlueprint";
    private const string InterfaceSetPermissions = "SetPermissions";
    private const string InterfaceMirrorBlueprintX = "MirrorBlueprintX";
    private const string InterfaceMirrorBlueprintZ = "MirrorBlueprintZ";
    private const string InterfaceStartBuilding = "StartBuilding";

    private enum WindowType
    {
        Main,
        BlueprintList,
    }

    #region SpawnWindow
    public override void SpawnWindow(SegmentEntity targetEntity)
    {
        BlueprintBuilder builder = targetEntity as BlueprintBuilder;
        builder = builder.GetControllingBuilder();
        if (builder == null)
            return;

        switch (CurrentWindow)
        {
            case WindowType.Main:
                this.SpawnMain(builder);
                break;
            case WindowType.BlueprintList:
                this.SpawnList(builder);
                break;
        }
        networkredraw = false;
    }

    private void SpawnMain(BlueprintBuilder builder)
    {
        this.manager.SetTitle("Blueprint Builder");
        // Mad Vandal's drop down list for BP selection - remake each time since it doesn't get new blueprints... will sort out later...
        if (BlueprintList == null)
        {
            BlueprintList = new UIDropDownList<Blueprint>(60, -60, 280, 30, GenericMachinePanelScript.instance.gameObject, Blueprint.LoadedBlueprints.Values.ToList(), "Name", "Select Blueprint", null, 10);
            BlueprintList.SelectionChanged = this.DropDownBlueprint;
        }
        else
        {
            BlueprintList.RefreshList(Blueprint.LoadedBlueprints.Values.ToList());
            BlueprintList.SelectedValue = builder.CurrentBlueprint;
        }
        BlueprintList.SetActive(true);
        BlueprintList.Data = builder;
        BlueprintList.Update();

        //this.manager.AddButton("pickblueprint", "Select Blueprint", 15, 0);
        //this.manager.AddBigLabel("bpnamelabel", builder.CurrentBPName, Color.white, 160, 0);
        this.manager.AddButton("rotateCW", "Rotate CW", 15, 50);
        this.manager.AddButton("rotateCCW", "Rotate CCW", 150, 50);
        this.manager.AddButton("mirrormodex", "Mirror X", 15, 105);
        this.manager.AddButton("mirrormodez", "Mirror Z", 150, 105);
        this.manager.AddButton("permissions", "Permissions", 15, 160);
        string perms = "unknown?";
        switch (builder.BuildAllowance)
        {
            case BlueprintBuilder.BuildType.RequireAir:
                perms = "Require Air";
                break;
            case BlueprintBuilder.BuildType.FillAirOnly:
                perms = "Fill Air";
                break;
            case BlueprintBuilder.BuildType.ReplaceAll:
                perms = "Replace All!";
                break;
        }
        this.manager.AddBigLabel("permlabel", perms, Color.white, 160, 160);

        // TODO: Update vanilla code to support updating button labels! Finally a reason to do it!!
        string build;
        switch (builder.BuildMode)
        {
            case BlueprintBuilder.BuildState.Blocked:
                build = "Rescan";
                break;
            case BlueprintBuilder.BuildState.ConfiguringBlueprint:
                build = "Configuring...";
                break;
            case BlueprintBuilder.BuildState.AwaitingStart:
                build = "Build";
                break;
            case BlueprintBuilder.BuildState.Idle:
                if (builder.CurrentBlueprint != null)
                    build = "Complete!";
                else
                    build = "Not ready!";
                break;
            case BlueprintBuilder.BuildState.Building:
                build = "Building...";  // TODO: Add pausing functionality
                break;
            default:
                build = "Not Ready!";
                break;
        }
        this.manager.AddButton("startbuilding", build, 80, 215);
        this.manager.AddBigLabel("powercost", "Power Required: " + builder.BPPowerCost, Color.white, 20, 270);
    }

    private void SpawnList(BlueprintBuilder builder)
    {
        this.manager.SetTitle("Select Blueprint");
        this.manager.AddButton("cancel", "Cancel", 100, 0);

        int count = Blueprint.LoadedBlueprints.Count;
        List<string> keys = Blueprint.LoadedBlueprints.Keys.ToList();
        for (int n = 0; n < count; n++)
        {
            this.manager.AddButton("blueprint" + keys[n], keys[n], 100, 55 + 50 * n);
        }
    }
    #endregion

    #region UpdateMachine
    public override void UpdateMachine(SegmentEntity targetEntity)
    {
        BlueprintBuilder builder = targetEntity as BlueprintBuilder;
        builder = builder.GetControllingBuilder();
        if (builder == null)
            return;

        // Only need in the drop down which has the code baked in
        //GenericMachinePanelScript.instance.Scroll_Bar.GetComponent<UIScrollBar>().scrollValue -= Input.GetAxis("Mouse ScrollWheel");

        if (BlueprintList.Visible)
            BlueprintList.Update();

        if (networkredraw)
            this.manager.RedrawWindow();
        if (!dirty)
            return;

        switch (this.CurrentWindow)
        {
            case WindowType.Main:
                this.UpdateMain(builder);
                break;
        }
    }

    private void UpdateMain(BlueprintBuilder builder)
    {
        //this.manager.UpdateLabel("bpnamelabel", builder.CurrentBPName, Color.white);
        this.manager.UpdateLabel("powercost", "Power Required: " + builder.BPPowerCost, Color.white);
        string perms = "unknown?";
        switch (builder.BuildAllowance)
        {
            case BlueprintBuilder.BuildType.RequireAir:
                perms = "Require Air";
                break;
            case BlueprintBuilder.BuildType.FillAirOnly:
                perms = "Fill Air";
                break;
            case BlueprintBuilder.BuildType.ReplaceAll:
                perms = "Replace All!";
                break;
        }
        this.manager.UpdateLabel("permlabel", perms, Color.white);
        dirty = false;
    }
    #endregion


    public override bool ButtonClicked(string name, SegmentEntity targetEntity)
    {
        BlueprintBuilder builder = targetEntity as BlueprintBuilder;
        builder = builder.GetControllingBuilder();
        if (builder == null)
            return false;

        if (name == "pickblueprint")
        {
            this.CurrentWindow = WindowType.BlueprintList;
            this.manager.RedrawWindow();
            return true;
        }
        else if (name.StartsWith("blueprint"))
        {
            string bpname = name.Substring(9);
            BlueprintBuilderWindow.SelectBlueprint(builder, bpname, WorldScript.mLocalPlayer);
            this.CurrentWindow = WindowType.Main;
            this.manager.RedrawWindow();
        }
        else if (name == "cancel")
        {
            this.CurrentWindow = WindowType.Main;
            this.manager.RedrawWindow();
            return true;
        }
        else if (name == "rotateCW")
        {
            if (builder.CurrentBlueprint == null)
                return false;
            BlueprintBuilder.RotState newstate = BlueprintBuilder.RotState.Original;
            switch (builder.BPRotation)
            {
                case BlueprintBuilder.RotState.Original:
                    newstate = BlueprintBuilder.RotState.Rotated90;
                    break;
                case BlueprintBuilder.RotState.Rotated90:
                    newstate = BlueprintBuilder.RotState.Rotated180;
                    break;
                case BlueprintBuilder.RotState.Rotated180:
                    newstate = BlueprintBuilder.RotState.Rotated270;
                    break;
                case BlueprintBuilder.RotState.Rotated270:
                    newstate = BlueprintBuilder.RotState.Original;
                    break;
            }
            BlueprintBuilderWindow.RotateBlueprint(builder, newstate);
            return true;
        }
        else if (name == "rotateCCW")
        {
            if (builder.CurrentBlueprint == null)
                return false;
            BlueprintBuilder.RotState newstate = BlueprintBuilder.RotState.Original;
            switch (builder.BPRotation)
            {
                case BlueprintBuilder.RotState.Original:
                    newstate = BlueprintBuilder.RotState.Rotated270;
                    break;
                case BlueprintBuilder.RotState.Rotated90:
                    newstate = BlueprintBuilder.RotState.Original;
                    break;
                case BlueprintBuilder.RotState.Rotated180:
                    newstate = BlueprintBuilder.RotState.Rotated90;
                    break;
                case BlueprintBuilder.RotState.Rotated270:
                    newstate = BlueprintBuilder.RotState.Rotated180;
                    break;
            }
            BlueprintBuilderWindow.RotateBlueprint(builder, newstate);
            return true;
        }
        else if (name == "permissions")
        {
            BlueprintBuilder.BuildType perms = BlueprintBuilder.BuildType.RequireAir;
            switch (builder.BuildAllowance)
            {
                case BlueprintBuilder.BuildType.RequireAir:
                    perms = BlueprintBuilder.BuildType.FillAirOnly;
                    break;
                case BlueprintBuilder.BuildType.FillAirOnly:
                    perms = BlueprintBuilder.BuildType.ReplaceAll;
                    break;
                case BlueprintBuilder.BuildType.ReplaceAll:
                    perms = BlueprintBuilder.BuildType.RequireAir;
                    break;
            }
            BlueprintBuilderWindow.SetPermissions(builder, perms);
            return true;
        }
        else if (name == "mirrormodex")
        {
            if (builder.CurrentBlueprint == null)
                return false;
            BlueprintBuilderWindow.MirrorBlueprintX(builder, !builder.BPMirroredX);
            return true;
        }
        else if (name == "mirrormodez")
        {
            if (builder.CurrentBlueprint == null)
                return false;
            BlueprintBuilderWindow.MirrorBlueprintZ(builder, !builder.BPMirroredZ);
            return true;
        }
        else if (name == "startbuilding")
        {
            if (builder.BuildMode == BlueprintBuilder.BuildState.Blocked && builder.CurrentBlueprint != null)
            {
                BlueprintBuilderWindow.SelectBlueprint(builder, builder.CurrentBPName, WorldScript.mLocalPlayer);
                networkredraw = true;
                return true;
            }
            else if (builder.BuildMode == BlueprintBuilder.BuildState.AwaitingStart)
            {
                BlueprintBuilderWindow.StartBuilding(builder);
                return true;
            }
        }

        return false;
    }

    public void DropDownBlueprint(object builder, int index, Blueprint bp)
    {
        BlueprintBuilderWindow.SelectBlueprint(builder as BlueprintBuilder, bp.Name, WorldScript.mLocalPlayer);
        this.CurrentWindow = WindowType.Main;
        this.manager.RedrawWindow();
    }

    public override void OnClose(SegmentEntity targetEntity)
    {
        //this.BlueprintList.Visible = false;
        BlueprintList.SetActive(false);
        //BlueprintList.Destroy();
    }

    #region Networking
    public static void SelectBlueprint(BlueprintBuilder builder, string bpname, Player player)
    {
        if (!WorldScript.mbIsServer)
        {
            //Holding period may allow for receiving the blueprint back first?  Probably bad to rely on that timing...
            NetworkManager.instance.SendInterfaceCommand(InterfaceName, InterfaceSelectBlueprint, bpname, null, builder, 0f);
            Blueprint bp = null;
            
            if (!string.IsNullOrEmpty(bpname) && Blueprint.LoadedBlueprints.TryGetValue(bpname, out bp) && bp.Blocks == null)
                ModManager.ModSendClientCommToServer("steveman0.RequestBlueprint", bpname);
            else if (bp == null || bp.Blocks == null)
                Debug.LogWarning("Client tried selecting a blueprint with name " + (!string.IsNullOrEmpty(bpname) ? bpname : " null") + " but couldn't complete request");
            builder.SetBlueprint(bp);
        }
        else
        {
            Blueprint bp;
            if (!string.IsNullOrEmpty(bpname) && Blueprint.LoadedBlueprints.TryGetValue(bpname, out bp) && bp != null)
                builder.SetBlueprint(bp);
            else
                Debug.LogWarning("BlueprintBuilderWindow could not find a blueprint matching the specified name '" + bpname + "'. Was it removed and the client doesn't know?");
        }
    }

    public static void RotateBlueprint(BlueprintBuilder builder, BlueprintBuilder.RotState newstate)
    {
        builder.BPRotation = newstate;
        builder.SetBlueprint(builder.CurrentBlueprint);
        builder.MarkDirtyDelayed();
        if (!WorldScript.mbIsServer)
            NetworkManager.instance.SendInterfaceCommand(InterfaceName, InterfaceRotateBlueprint, ((int)newstate).ToString(), null, builder, 0f);
    }

    public static void SetPermissions(BlueprintBuilder builder, BlueprintBuilder.BuildType perms)
    {
        builder.BuildAllowance = perms;
        builder.RequestImmediateNetworkUpdate();
        builder.MarkDirtyDelayed();
        dirty = true;
        if (!WorldScript.mbIsServer)
            NetworkManager.instance.SendInterfaceCommand(InterfaceName, InterfaceSetPermissions, ((int)perms).ToString(), null, builder, 0f);
    }

    public static void MirrorBlueprintX(BlueprintBuilder builder, bool mirrored)
    {
        builder.BPMirroredX = mirrored;
        builder.SetBlueprint(builder.CurrentBlueprint);
        builder.MarkDirtyDelayed();
        if (!WorldScript.mbIsServer)
            NetworkManager.instance.SendInterfaceCommand(InterfaceName, InterfaceMirrorBlueprintX, mirrored ? "1" : "0", null, builder, 0f);
    }

    public static void MirrorBlueprintZ(BlueprintBuilder builder, bool mirrored)
    {
        builder.BPMirroredZ = mirrored;
        builder.SetBlueprint(builder.CurrentBlueprint);
        builder.MarkDirtyDelayed();
        if (!WorldScript.mbIsServer)
            NetworkManager.instance.SendInterfaceCommand(InterfaceName, InterfaceMirrorBlueprintZ, mirrored ? "1" : "0", null, builder, 0f);
    }

    public static void StartBuilding(BlueprintBuilder builder)
    {
        builder.StartBuilding();
        builder.MarkDirtyDelayed();
        if (!WorldScript.mbIsServer)
            NetworkManager.instance.SendInterfaceCommand(InterfaceName, InterfaceStartBuilding, null, null, builder, 0f);
    }

    public static NetworkInterfaceResponse HandleNetworkCommand(Player player, NetworkInterfaceCommand nic)
    {
        BlueprintBuilder builder = nic.target as BlueprintBuilder;

        switch (nic.command)
        {
            case InterfaceSelectBlueprint:
                BlueprintBuilderWindow.SelectBlueprint(builder, nic.payload, player);
                break;
            case InterfaceRotateBlueprint:
                int state;
                int.TryParse(nic.payload ?? "99", out state);
                if (state < 4)
                    BlueprintBuilderWindow.RotateBlueprint(builder, (BlueprintBuilder.RotState)state);
                break;
            case InterfaceSetPermissions:
                int perms;
                int.TryParse(nic.payload ?? "99", out perms);
                if (perms < 3)
                    BlueprintBuilderWindow.SetPermissions(builder, (BlueprintBuilder.BuildType)perms);
                break;
            case InterfaceMirrorBlueprintX:
                int mirror;
                int.TryParse(nic.payload ?? "-1", out mirror);
                if (mirror != -1)
                    BlueprintBuilderWindow.MirrorBlueprintX(builder, mirror == 1);
                break;
            case InterfaceMirrorBlueprintZ:
                int mirrorz;
                int.TryParse(nic.payload ?? "-1", out mirrorz);
                if (mirrorz != -1)
                    BlueprintBuilderWindow.MirrorBlueprintX(builder, mirrorz == 1);
                break;
            case InterfaceStartBuilding:
                BlueprintBuilderWindow.StartBuilding(builder);
                break;
        }

        return new NetworkInterfaceResponse
        {
            entity = builder,
            inventory = player.mInventory
        };
    }
    #endregion
}

