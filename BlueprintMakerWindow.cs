using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class BlueprintMakerWindow : BaseMachineWindow
{
    public static CubeCoord Start = CubeCoord.Invalid;
    public static CubeCoord Finish = CubeCoord.Invalid;
    private WindowType CurrentWindow = WindowType.Main;
    private string EntryString;
    private int Counter;
    public static bool dirty;
    private string bpName;
    private CoordsCapture State;

    private enum WindowType
    {
        Main,
        Name,
        Confirmation,
        BlueprintList,
    }

    private enum CoordsCapture
    {
        None,
        Start,
        Finish,
    }

    #region SpawnWindow
    public override void SpawnWindow(SegmentEntity targetEntity)
    {
        this.HandleCoordsCapture();
        switch (CurrentWindow)
        {
            case WindowType.Main:
                this.SpawnMain();
                break;
            case WindowType.Name:
                this.SpawnName();
                break;
            case WindowType.Confirmation:
                this.SpawnConfirmation();
                break;
            case WindowType.BlueprintList:
                this.SpawnList();
                break;
        }
    }

    private void SpawnMain()
    {
        const int xoffset = 25;
        this.manager.SetTitle("Blueprint Maker");
        this.manager.AddBigLabel("startcoords", Start == CubeCoord.Invalid ? "Undefined" : "Start Set!", Color.white, xoffset + 150, 0);
        this.manager.AddBigLabel("finishcoords", Finish == CubeCoord.Invalid ? "Undefined" : "Finish Set!", Color.white, xoffset + 150, 60);
        this.manager.AddButton("setstart", "Set Start Coords", xoffset + 0, 0);
        this.manager.AddButton("setfinish", "Set Finish Coords", xoffset + 0, 60);
        this.manager.AddBigLabel("bpname", string.IsNullOrEmpty(bpName) ? "Unnamed" : bpName, Color.white, xoffset + 150, 120);
        this.manager.AddButton("setname", "Set Name", xoffset + 0, 120);
        this.manager.AddButton("savebutton", "Save Blueprint", xoffset + 0, 200);
        this.manager.AddButton("showarea", "Show Area", xoffset + 150, 200);
        this.manager.AddButton("bplist", "List Blueprints", xoffset + 0, 260);
        this.manager.AddButton("closebutton", "Done", xoffset + 150, 260);
    }

    private void SpawnConfirmation()
    {
        this.manager.SetTitle("Overwrite " + this.bpName + "?");
        this.manager.AddBigLabel("confirmtitle", "A Blueprint with this name exists!  Overwrite?", Color.red, 0, 0);
        this.manager.AddButton("overwrite", "Overwrite", 25, 60);
        this.manager.AddButton("cancel", "Cancel", 175, 60);
    }

    private void SpawnList()
    {
        this.manager.SetTitle("Current Blueprints");
        this.manager.AddButton("cancel", "Done", 75, 0);
        List<string> names = Blueprint.LoadedBlueprints.Keys.ToList();
        int count = names.Count;
        for (int n = 0; n < count; n++)
        {
            this.manager.AddBigLabel("bpname" + n, names[n], Color.white, 0, 60 + n * 30);
        }
    }

    private void SpawnName()
    {
        this.manager.SetTitle("Name Blueprint");
        this.manager.AddBigLabel("textheader", "Enter Blueprint Name", Color.white, 50, 40);

        UIManager.mbEditingTextField = true;
        UIManager.AddUIRules("TextEntry", UIRules.RestrictMovement | UIRules.RestrictLooking | UIRules.RestrictBuilding | UIRules.RestrictInteracting | UIRules.SetUIUpdateRate);
        GenericMachinePanelScript.instance.Scroll_Bar.GetComponent<UIScrollBar>().scrollValue = 0.0f;

        this.manager.AddButton("cancel", "Cancel", 100, 0);
        this.manager.AddBigLabel("nameentry", "_", Color.cyan, 50, 65);
        dirty = true;
    }
    #endregion

    private void HandleCoordsCapture()
    {
        if (this.State == CoordsCapture.None)
            return;
        long x = WorldScript.instance.localPlayerInstance.mPlayerBlockPicker.selectBlockX;
        long y = WorldScript.instance.localPlayerInstance.mPlayerBlockPicker.selectBlockY;
        long z = WorldScript.instance.localPlayerInstance.mPlayerBlockPicker.selectBlockZ;

        // Add adjustment for when player is holding shift!!
        if (Input.GetKey(KeyCode.LeftShift))
        {
            switch (WorldScript.instance.localPlayerInstance.mPlayerBlockPicker.selectFaceDir)
            {
                case CubeHelper.EAST:
                    x++;
                    break;
                case CubeHelper.NORTH:
                    z++;
                    break;
                case CubeHelper.WEST:
                    x--;
                    break;
                case CubeHelper.SOUTH:
                    z--;
                    break;
                case CubeHelper.TOP:
                    y++;
                    break;
                case CubeHelper.BOT:
                    y--;
                    break;
            }
        }
        if (this.State == CoordsCapture.Start)
            Start = new CubeCoord(x, y, z);
        else
            Finish = new CubeCoord(x, y, z);
        this.State = CoordsCapture.None;
        BlueprintBuilderMod.ZoneTimer = 30f;
        BlueprintBuilderMod.ZoneActive = true;
        BlueprintBuilderMod.ZoneDirty = true;
    }

    #region UpdateMachine
    public override void UpdateMachine(SegmentEntity targetEntity)
    {
        if (CurrentWindow == WindowType.BlueprintList)
            GenericMachinePanelScript.instance.Scroll_Bar.GetComponent<UIScrollBar>().scrollValue -= Input.GetAxis("Mouse ScrollWheel");

        if (!dirty)
            return;

        switch (CurrentWindow)
        {
            case WindowType.Main:
                dirty = false;
                break;
            case WindowType.Name:
                this.UpdateName();
                break;
            case WindowType.Confirmation:
                dirty = false;
                break;
            case WindowType.BlueprintList:
                dirty = false;
                break;
        }
    }
    
    private void UpdateName()
    {
        this.Counter++;
        foreach (char c in Input.inputString)
        {
            if (c == "\b"[0])  //Backspace
            {
                if (this.EntryString.Length != 0)
                    this.EntryString = this.EntryString.Substring(0, this.EntryString.Length - 1);
            }
            else if (c == "\n"[0] || c == "\r"[0]) //Enter or Return
            {
                this.bpName = Util.MakeDiskSafe(this.EntryString);
                this.CurrentWindow = WindowType.Main;
                this.EntryString = "";
                UIManager.mbEditingTextField = false;
                UIManager.RemoveUIRules("TextEntry");
                this.manager.RedrawWindow();
                return;
            }
            else
                this.EntryString += c;
        }
        this.manager.UpdateLabel("nameentry", this.EntryString + (this.Counter % 20 > 10 ? "_" : ""), Color.cyan);
        dirty = true;
        return;
    }
    #endregion

    public override bool ButtonClicked(string name, SegmentEntity targetEntity)
    {
        if (name == "cancel")
        {
            this.CurrentWindow = WindowType.Main;
            this.EntryString = "";
            UIManager.mbEditingTextField = false;
            UIManager.RemoveUIRules("TextEntry");
            this.manager.RedrawWindow();
            return true;
        }
        else if (name == "setstart")
        {
            this.State = CoordsCapture.Start;
            this.OnClose(null);
            GenericMachinePanelScript.instance.Hide();
            return true;
        }
        else if (name == "setfinish")
        {
            this.State = CoordsCapture.Finish;
            this.OnClose(null);
            GenericMachinePanelScript.instance.Hide();
            return true;
        }
        else if (name == "setname")
        {
            this.CurrentWindow = WindowType.Name;
            this.manager.RedrawWindow();
            return true;
        }
        else if (name == "closebutton")
        {
            this.OnClose(null);
            GenericMachinePanelScript.instance.Hide();
            return true;
        }
        else if (name == "savebutton")
        {
            if (string.IsNullOrEmpty(this.bpName) || Start == CubeCoord.Invalid || Finish == CubeCoord.Invalid)
                return false;
            // Check for overwritting!!!
            if (Blueprint.LoadedBlueprints.ContainsKey(this.bpName))
            {
                this.CurrentWindow = WindowType.Confirmation;
                this.manager.RedrawWindow();
                return true;
            }
            CreateBlueprint(this.bpName);
            Start = CubeCoord.Invalid;
            Finish = CubeCoord.Invalid;
            BlueprintBuilderMod.ZoneActive = false;
            this.bpName = string.Empty;
            this.manager.RedrawWindow();
            return true;
        }
        else if (name == "overwrite")
        {
            if (string.IsNullOrEmpty(this.bpName) || Start == CubeCoord.Invalid || Finish == CubeCoord.Invalid)
                return false;
            CreateBlueprint(this.bpName);
            Start = CubeCoord.Invalid;
            Finish = CubeCoord.Invalid;
            BlueprintBuilderMod.ZoneActive = false;
            this.bpName = string.Empty;
            this.CurrentWindow = WindowType.Main;
            this.manager.RedrawWindow();
            return true;
        }
        else if (name == "showarea")
        {
            BlueprintBuilderMod.ZoneTimer = 30f;
            BlueprintBuilderMod.ZoneActive = true;
            BlueprintBuilderMod.ZoneDirty = true;
            this.OnClose(null);
            GenericMachinePanelScript.instance.Hide();
        }
        else if (name == "bplist")
        {
            this.CurrentWindow = WindowType.BlueprintList;
            this.manager.RedrawWindow();
            return true;
        }
        return false;
    }

    public override void OnClose(SegmentEntity targetEntity)
    {
        this.EntryString = "";
        UIManager.mbEditingTextField = false;
        UIManager.RemoveUIRules("TextEntry");
        this.CurrentWindow = WindowType.Main;
    }

    public static void CreateBlueprint(string name)
    {
        Blueprint bp = new Blueprint(Start, Finish, name);
        if (WorldScript.mbIsServer)
        {
            bp.SaveBlueprint();
            if (!Blueprint.LoadedBlueprints.ContainsKey(bp.Name))
                Blueprint.LoadedBlueprints.Add(name, bp);
        }
        else
            ModManager.ModSendClientCommToServer("SendBlueprintToServer", bp);
    }
}

