using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Blueprint
{
    public string Name { get; protected set; }
    public CubeBlock[,,] Blocks;
    public int SizeX;
    public int SizeY;
    public int SizeZ;
    public IFCFileHandler mFile;
    public static Dictionary<string, Blueprint> LoadedBlueprints = new Dictionary<string, Blueprint>();

    //Keyed with Type << 16 | Value
    public Dictionary<int, string> ModBlocks = new Dictionary<int, string>();

    public Blueprint(CubeCoord start, CubeCoord finish, string name)
    {
        this.SizeX = System.Math.Abs((int)finish.x - (int)start.x) + 1;
        this.SizeY = System.Math.Abs((int)finish.y - (int)start.y) + 1;
        this.SizeZ = System.Math.Abs((int)finish.z - (int)start.z) + 1;
        this.Blocks = new CubeBlock[SizeX, SizeY, SizeZ];
        this.Name = name;

        //You know make the thing here
        //Make sure we start from the lowest coordinates to standardize orientation!
        long xcube = System.Math.Min(start.x, finish.x);
        long ycube = System.Math.Min(start.y, finish.y);
        long zcube = System.Math.Min(start.z, finish.z);
        long zcorner = zcube;
        long ycorner = ycube;

        //int xl = finish.x > start.x ? 1 : -1;
        //int yl = 1;  //finish.y > start.y ? 1 : -1;  -> Y should always be referenced from the bottom!  Flipping it is silly!
        //int zl = finish.z > start.z ? 1 : -1;
        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    //48897 first mod type
                    ushort type;
                    ushort val;
                    byte flags;
                    // This might be really slow for large blueprints!  I should get the segment and pull as many values from it before out of range
                    type = this.GetCube(xcube, ycube, zcube, out val, out flags);
                    // Don't allow Blueprinting things you can't build with!
                    if (TerrainData.GetHardness(type, val) >= 250)
                    {
                        type = 1;
                        val = 0;
                    }
                    if (type >= 48897 && !ModBlocks.ContainsKey(type << 16 | val)) //Mod Block!
                    {
                        ModCubeMap map = ModManager.mModMappings.CubeTypes.Where(a => a.CubeType == type).FirstOrDefault();
                        if (map == null)
                            Debug.LogWarning("Blueprint found suspected Mod block with cube type '" + type + "' but it wasn't in the mod mappings?");
                        else
                        {
                            ModCubeValueMap valmap = map.Values.Where(b => b.Value == val).FirstOrDefault();
                            if (valmap == null)
                                ModBlocks.Add(type << 16 | val, map.Key);
                            else
                                ModBlocks.Add(type << 16 | val, map.Key + ".." + valmap.Key);
                        }
                    }
                    // Silly hack to get the lab center block
                    if (type == 535)
                    {
                        SegmentEntity entity = this.GetEntity(xcube, ycube, zcube);
                        Laboratory lab = entity as Laboratory;
                        if (lab != null && lab.mbIsCenter)
                            val = 1;
                    }
                    this.Blocks[x, y, z] = new CubeBlock(type, val, flags);
                    zcube++;
                }
                zcube = zcorner;
                ycube++;
            }
            ycube = ycorner;
            xcube++;
        }
    }

    public Blueprint(string name, int sizex, int sizey, int sizez)
    {
        this.Name = name;
        this.SizeX = sizex;
        this.SizeY = sizey;
        this.SizeZ = sizez;
        this.Blocks = new CubeBlock[sizex, sizey, sizez];
    }

    public Blueprint()
    {
        this.Name = "UNNAMED";
    }

    public Blueprint(string name)
    {
        this.Name = name;
    }

    public void SaveBlueprint()
    {
        if (WorldScript.mbIsServer && this.mFile == null)
        {
            string baseFileName = BlueprintBuilderMod.BlueprintPath + "BP_" + this.Name + ".dat";

            this.mFile = WorldScript.instance.mDiskThread.RegisterManagedFile(new ManagedFileSaveMethod(this.Write), new ManagedFileLoadMethod(this.FileRead), new ManagedFileConversionMethod(this.FileConversion), baseFileName);
            this.mFile.MarkReady();
            this.mFile.MarkDirty();
        }
        else if (!WorldScript.mbIsServer)
            Debug.LogWarning("SaveBlueprint should only be called by the server!");
        else
            this.mFile.MarkDirty();
    }

    public void LoadBlueprint(string filename)
    {
        if (WorldScript.mbIsServer && this.mFile == null)
        {
            this.mFile = WorldScript.instance.mDiskThread.RegisterManagedFile(new ManagedFileSaveMethod(this.Write), new ManagedFileLoadMethod(this.FileRead), new ManagedFileConversionMethod(this.FileConversion), filename);
            this.mFile.RequestLoad();
        }
        else if (!WorldScript.mbIsServer)
            Debug.LogWarning("LoadBlueprint should only be called by the server!");
    }

    public bool Write(BinaryWriter writer)
    {
        //long pos = writer.BaseStream.Position;

        //version
        writer.Write(1);

        if (string.IsNullOrEmpty(Name))
            writer.Write(string.Empty);
        else
            writer.Write(Name);
        writer.Write(SizeX);
        writer.Write(SizeY);
        writer.Write(SizeZ);

        List<int> keys = ModBlocks.Keys.ToList();
        int count = keys.Count;
        writer.Write(count);
        for (int n = 0; n < count; n++)
        {
            writer.Write(keys[n]);
            writer.Write(ModBlocks[keys[n]]);
        }

        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    CubeBlock block = Blocks[x, y, z];
                    if (block.Type == 0)
                    {
                        Debug.LogError("Blueprint attempted to write with an uninitialized block entry.  Filling with air");
                        writer.Write((ushort)1);
                        writer.Write((ushort)0);
                        writer.Write((byte)0);
                    }
                    else
                    {
                        writer.Write(block.Type);
                        writer.Write(block.Value);
                        writer.Write(block.Flags);
                    }
                }
            }
        }
        //Debug.Log("Blueprint writing with name " + (string.IsNullOrEmpty(Name) ? "NoName" : Name) + " with byte count: " + (writer.BaseStream.Position - pos).ToString());
        return true;
    }

    public FCFileLoadAttemptResult FileRead(BinaryReader reader, bool isBackup)
    {
        //Read data
        int version = reader.ReadInt32();
        string name = reader.ReadString();
        int sizex = reader.ReadInt32();
        int sizey = reader.ReadInt32();
        int sizez = reader.ReadInt32();

        Blueprint bp = new Blueprint(name, sizex, sizey, sizez);

        int modblockcount = reader.ReadInt32();
        for (int n = 0; n < modblockcount; n++)
        {
            int modblockdata = reader.ReadInt32();
            string modblockkeystring = reader.ReadString();

            if (!bp.ModBlocks.ContainsKey(modblockdata))
                bp.ModBlocks.Add(modblockdata, modblockkeystring);
            else
                Debug.LogWarning("Blueprint with name '" + name + "' loaded in with mod blocks with duplicate block type data!  Ignoring duplicate entry.");
        }

        for (int x = 0; x < sizex; x++)
        {
            for (int y = 0; y < sizey; y++)
            {
                for (int z = 0; z < sizez; z++)
                {
                    ushort type = reader.ReadUInt16();
                    ushort val = reader.ReadUInt16();
                    byte flags = reader.ReadByte();
                    bp.Blocks[x, y, z] = new CubeBlock(type, val, flags);
                }
            }
        }

        if (!Blueprint.LoadedBlueprints.ContainsKey(name))
            Blueprint.LoadedBlueprints.Add(name, bp);
        else
        {
            Debug.LogWarning("Blueprint file read in with duplicate name! Blueprint file names must match their blueprint name!");
            return FCFileLoadAttemptResult.FailedAbort;
        }
        return FCFileLoadAttemptResult.Successful;
    }

    public void FileConversion()
    {
        //Not used currently
    }

    public static void SendToServer(BinaryWriter writer, object blueprint)
    {
        (blueprint as Blueprint).Write(writer);
    }

    public static void SendToClient(BinaryWriter writer, Player player, object blueprint)
    {
        (blueprint as Blueprint).Write(writer);
    }

    public static void ServerRead(Lidgren.Network.NetIncomingMessage message, Player player)
    {
        //read blueprint
        int version = message.ReadInt32();
        string name = message.ReadString();
        int sizex = message.ReadInt32();
        int sizey = message.ReadInt32();
        int sizez = message.ReadInt32();

        Blueprint bp = new Blueprint(name, sizex, sizey, sizez);

        int modblockcount = message.ReadInt32();
        for (int n = 0; n < modblockcount; n++)
        {
            int modblockdata = message.ReadInt32();
            string modblockkeystring = message.ReadString();

            if (!bp.ModBlocks.ContainsKey(modblockdata))
                bp.ModBlocks.Add(modblockdata, modblockkeystring);
            else
                Debug.LogWarning("Blueprint with name '" + name + "' loaded in with mod blocks with duplicate block type data!  Ignoring duplicate entry.");
        }

        for (int x = 0; x < sizex; x++)
        {
            for (int y = 0; y < sizey; y++)
            {
                for (int z = 0; z < sizez; z++)
                {
                    ushort type = message.ReadUInt16();
                    ushort val = message.ReadUInt16();
                    byte flags = message.ReadByte();
                    bp.Blocks[x, y, z] = new CubeBlock(type, val, flags);
                }
            }
        }

        if (player.mBuildPermission == eBuildPermission.MutedVisitor || player.mBuildPermission == eBuildPermission.Visitor)
            return;
        
        //store blueprint
        if (!LoadedBlueprints.ContainsKey(name))
        {
            LoadedBlueprints.Add(name, bp);
            bp.SaveBlueprint();
        }
        else
            Debug.LogWarning("Server tried to add client sent blueprint but the server already had a blueprint of that name!");

        //Clients will need to know about the new BP!!
        ModManager.ModSendServerCommToClient("steveman0.SendNewBlueprintToClient", null, bp);
    }

    public static void ClientRead(Lidgren.Network.NetIncomingMessage message)
    {
        //long pos = message.PositionInBytes;
        int version = message.ReadInt32();
        string name = message.ReadString();
        int sizex = message.ReadInt32();
        int sizey = message.ReadInt32();
        int sizez = message.ReadInt32();

        Blueprint bp = new Blueprint(name, sizex, sizey, sizez);
        bp.Blocks = new CubeBlock[sizex, sizey, sizez];

        int modblockcount = message.ReadInt32();
        for (int n = 0; n < modblockcount; n++)
        {
            int modblockdata = message.ReadInt32();
            string modblockkeystring = message.ReadString();

            if (!bp.ModBlocks.ContainsKey(modblockdata))
                bp.ModBlocks.Add(modblockdata, modblockkeystring);
            else
                Debug.LogWarning("Blueprint with name '" + name + "' loaded in with mod blocks with duplicate block type data!  Ignoring duplicate entry.");
        }

        for (int x = 0; x < sizex; x++)
        {
            for (int y = 0; y < sizey; y++)
            {
                for (int z = 0; z < sizez; z++)
                {
                    ushort type = message.ReadUInt16();
                    ushort val = message.ReadUInt16();
                    byte flags = message.ReadByte();
                    bp.Blocks[x, y, z] = new CubeBlock(type, val, flags);
                }
            }
        }

        if (LoadedBlueprints.ContainsKey(name))
            LoadedBlueprints[name] = bp;
        else
            Debug.LogWarning("Client received Blueprint with no matching name in the Blueprint list");
        //Debug.Log("Blueprint client reading with name " + (string.IsNullOrEmpty(name) ? "NoName" : name) + " with byte count: " + (message.PositionInBytes - pos).ToString());
    }

    public static void RequestBlueprintList(BinaryWriter writer, object empty)
    {
        //Debug.Log("Client requesting blueprint list write step");
        //No data required
    }

    public static void AnswerListRequest(Lidgren.Network.NetIncomingMessage message, Player player)
    {
        //Debug.Log("Server answering list request write step");
        ModManager.ModSendServerCommToClient("steveman0.SendBlueprintList", player);
    }

    public static void RequestBlueprint(BinaryWriter writer, object bpname)
    {
        //Debug.Log("Client requesting blueprint write step");
        string name = bpname as string;
        if (!string.IsNullOrEmpty(name))
            writer.Write(name);
        else
            writer.Write(string.Empty);
    }

    public static void AnswerBlueprintRequest(Lidgren.Network.NetIncomingMessage message, Player player)
    {
        //Debug.Log("Server answering blueprint request write step");
        string bpname = message.ReadString();
        Blueprint bp;
        if (!string.IsNullOrEmpty(bpname) && Blueprint.LoadedBlueprints.TryGetValue(bpname, out bp) && bp != null)
            ModManager.ModSendServerCommToClient("steveman0.SendBlueprintToClient", player, bp);
    }

    public static void SendBlueprintList(BinaryWriter writer, Player player, object empty)
    {
        //Debug.Log("Server writing Blueprint List to client");
        List<string> blueprints = LoadedBlueprints.Keys.ToList();
        int count = blueprints.Count;
        writer.Write(count);
        for (int n = 0; n < count; n++)
        {
            writer.Write(blueprints[n]);
        }
    }

    public static void ReadBlueprintList(Lidgren.Network.NetIncomingMessage message)
    {
        int count = message.ReadInt32();
        for (int n = 0; n < count; n++)
        {
            string key = message.ReadString();
            if (!LoadedBlueprints.ContainsKey(key))
                LoadedBlueprints.Add(key, new Blueprint(key));
            else
                Debug.Log("Client reading blueprint list found list has a duplicate entry.  Did we send the list twice?");
        }
    }

    public static void SendNewBlueprintName(BinaryWriter writer, Player player, object name)
    {
        writer.Write(name as string);
    }

    public static void ReadNewBlueprintName(Lidgren.Network.NetIncomingMessage message)
    {
        string name = message.ReadString();

        //The client that sent the BP will also receive this message but we'll just ignore it.
        if (!LoadedBlueprints.ContainsKey(name))
            LoadedBlueprints.Add(name, null);
    }

    public ushort GetCube(long lTestX, long lTestY, long lTestZ, out ushort lValue, out byte lFlags)
    {
        if (lTestX < 100000L)
            Debug.LogError(("Error, Blueprint failed GetCube Check! X is " + lTestX));
        if (lTestY < 100000L)
            Debug.LogError(("Error, Blueprint failed GetCube Check! Y is " + lTestY));
        if (lTestZ < 100000L)
            Debug.LogError(("Error, Blueprint failed GetCube Check! Z is " + lTestZ));
        Segment segment;    
        long segX;
        long segY;
        long segZ;
        WorldHelper.GetSegmentCoords(lTestX, lTestY, lTestZ, out segX, out segY, out segZ);
        segment = WorldScript.instance.mPlayerFrustrum.GetSegment(lTestX, lTestY, lTestZ);
        if (segment == null)
            segment = WorldScript.instance.GetSegment(segX, segY, segZ);
        if (segment == null || !segment.mbInitialGenerationComplete || segment.mbDestroyed)
        {
            Debug.LogWarning("Blueprint GetCube could not find a segment for blueprint construction.  Block will be lost!");
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

    public SegmentEntity GetEntity(long x, long y, long z)
    {
        Segment segment;
        long segX;
        long segY;
        long segZ;
        WorldHelper.GetSegmentCoords(x, y, z, out segX, out segY, out segZ);
        segment = WorldScript.instance.mPlayerFrustrum.GetSegment(x, y, z);
        if (segment == null)
            segment = WorldScript.instance.GetSegment(segX, segY, segZ);
        if (segment == null || !segment.mbInitialGenerationComplete || segment.mbDestroyed)
        {
            Debug.LogWarning("Blueprint GetEntity could not find a segment for blueprint construction.  Lab might lose its hologram!");
            CentralPowerHub.mnMinecartX = x;
            CentralPowerHub.mnMinecartY = y;
            CentralPowerHub.mnMinecartZ = z;
            return null;
        }
        return segment.SearchEntity(x, y, z);
    }
}

public struct CubeBlock
{
    public ushort Type { get; } 
    public ushort Value { get; }
    public byte Flags { get; }

    public CubeBlock(ushort type, ushort val, byte flags)
    {
        this.Type = type;
        this.Value = val;
        this.Flags = flags;
    }
}

//public class ModBlock
//{
//    public uint Type { get; }
//    public uint Value { get; }
//    public string Key { get; }

//    public ModBlock(uint type, uint val, string key)
//    {
//        this.Type = type;
//        this.Value = val;
//        this.Key = key;
//    }
//}

