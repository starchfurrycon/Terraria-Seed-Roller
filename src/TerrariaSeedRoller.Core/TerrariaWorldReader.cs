using System.Text;

namespace TerrariaSeedRoller.Core;

internal static class TerrariaWorldReader
{
    internal const uint MinimumSupportedVersion = 269;
    internal const uint MaximumSupportedVersion = 326;

    public static ParsedWorld Read(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.SequentialScan);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        uint version = reader.ReadUInt32();
        if (version < MinimumSupportedVersion || version > MaximumSupportedVersion)
        {
            throw new InvalidDataException(
                $"Unsupported Terraria world format {version}. Supported: " +
                $"{MinimumSupportedVersion}-{MaximumSupportedVersion} (Terraria 1.4.3 through 1.4.5.8). No data was changed.");
        }

        string signature = Encoding.ASCII.GetString(ReadExact(reader, 7));
        byte fileType = reader.ReadByte();
        if ((signature != "relogic" && signature != "xindong") || fileType != 2)
            throw new InvalidDataException("The selected file is not a Terraria world.");

        _ = reader.ReadUInt32();
        _ = reader.ReadUInt64();
        short sectionCount = reader.ReadInt16();
        if (sectionCount < 4 || sectionCount > 32)
            throw new InvalidDataException($"Invalid world section count {sectionCount}.");

        int[] sections = new int[sectionCount];
        for (int i = 0; i < sections.Length; i++)
            sections[i] = reader.ReadInt32();
        ValidateSections(sections, stream.Length);

        bool[] frameImportant = ReadPackedBooleans(reader);
        if (stream.Position != sections[0])
            throw new InvalidDataException($"File-format header ended at {stream.Position}, expected {sections[0]}.");

        WorldMetadata metadata = ReadHeader(reader, version, sections[1]);
        stream.Position = sections[1];
        (WorldTileGrid grid, Dictionary<ushort, long> tileCounts, Dictionary<ushort, long> wallCounts) =
            ReadTiles(reader, metadata.Width, metadata.Height, version, frameImportant);
        if (stream.Position != sections[2])
        {
            throw new InvalidDataException(
                $"Tile section ended at {stream.Position}, expected {sections[2]}. " +
                "The file may have changed while being generated; no data was changed.");
        }

        List<WorldChest> chests = ReadChests(reader, version, grid);
        if (stream.Position != sections[3])
            throw new InvalidDataException($"Chest section ended at {stream.Position}, expected {sections[3]}.");

        return new ParsedWorld(
            fullPath,
            metadata,
            grid,
            chests,
            ReadOnlyCollections.Freeze(tileCounts),
            ReadOnlyCollections.Freeze(wallCounts),
            sections[1],
            sections[2],
            sections[3]);
    }

    private static WorldMetadata ReadHeader(BinaryReader reader, uint version, int expectedEnd)
    {
        string title = reader.ReadString();
        string seed = version == 179 ? reader.ReadInt32().ToString() : reader.ReadString();
        _ = reader.ReadUInt64();
        _ = ReadExact(reader, 16);
        int worldId = reader.ReadInt32();
        Skip(reader, 16);
        int height = reader.ReadInt32();
        int width = reader.ReadInt32();
        if (width is < 100 or > 20_000 || height is < 100 or > 5_000)
            throw new InvalidDataException($"Invalid world dimensions {width}x{height}.");

        int gameMode = reader.ReadInt32();
        bool drunk = reader.ReadBoolean();
        bool forTheWorthy = reader.ReadBoolean();
        bool celebration = reader.ReadBoolean();
        bool theConstant = reader.ReadBoolean();
        bool notTheBees = reader.ReadBoolean();
        bool remix = reader.ReadBoolean();
        bool noTraps = reader.ReadBoolean();
        bool zenith = reader.ReadBoolean();
        bool skyblock = version >= 302 && reader.ReadBoolean();

        Skip(reader, 8);
        if (version >= 284) Skip(reader, 8);
        Skip(reader, 1);
        Skip(reader, 3 * 4L);
        Skip(reader, 4 * 4L);
        Skip(reader, 3 * 4L);
        Skip(reader, 4 * 4L);
        Skip(reader, 3 * 4L);
        int spawnX = reader.ReadInt32();
        int spawnY = reader.ReadInt32();
        double worldSurface = reader.ReadDouble();
        double rockLayer = reader.ReadDouble();
        Skip(reader, 8);
        if (!double.IsFinite(worldSurface) || !double.IsFinite(rockLayer) ||
            worldSurface <= 0 || rockLayer <= worldSurface || rockLayer >= height)
            throw new InvalidDataException($"Invalid layer depths: surface={worldSurface}, rock={rockLayer}.");

        Skip(reader, 1 + 4 + 1 + 1);
        int dungeonX = reader.ReadInt32();
        int dungeonY = reader.ReadInt32();
        bool isCrimson = reader.ReadBoolean();
        Skip(reader, 10);
        if (version >= 118) Skip(reader, 1);
        Skip(reader, 7);
        Skip(reader, 2);
        Skip(reader, 1);
        Skip(reader, 4);
        bool hardMode = reader.ReadBoolean();
        if (version >= 257) Skip(reader, 1);
        Skip(reader, 3 * 4L + 8);
        if (version >= 118) Skip(reader, 8);
        if (version >= 113) Skip(reader, 1);
        Skip(reader, 1 + 4 + 4);
        Skip(reader, 3 * 4L);
        Skip(reader, 8);
        Skip(reader, 4 + 2 + 4);

        int anglerCount = ReadCount(reader.ReadInt32(), 10_000, "angler list");
        for (int i = 0; i < anglerCount; i++) _ = reader.ReadString();
        if (version >= 99) Skip(reader, 1);
        if (version >= 101) Skip(reader, 4);
        if (version >= 104) Skip(reader, 1);
        if (version >= 129) Skip(reader, 1);
        if (version >= 201) Skip(reader, 1);
        if (version >= 107) Skip(reader, 4);
        if (version >= 108) Skip(reader, 4);

        int killedMobCount = ReadCount(reader.ReadInt16(), 65_535, "banner kill list");
        Skip(reader, killedMobCount * 4L);
        if (version >= 289)
        {
            int claimableCount = ReadCount(reader.ReadInt16(), 65_535, "claimable banner list");
            Skip(reader, claimableCount * 2L);
        }
        if (version >= 128) Skip(reader, 1);
        if (version >= 131) Skip(reader, 9);
        if (version >= 140) Skip(reader, 9);
        if (version >= 170)
        {
            Skip(reader, 2 + 4);
            int partyNpcCount = ReadCount(reader.ReadInt32(), 10_000, "party NPC list");
            Skip(reader, partyNpcCount * 4L);
        }
        if (version >= 174) Skip(reader, 1 + 4 + 4 + 4);
        if (version >= 178) Skip(reader, 4);
        if (version > 194) Skip(reader, 1);
        if (version >= 215) Skip(reader, 1);
        if (version > 195) Skip(reader, 3);
        if (version >= 204) Skip(reader, 1);
        if (version >= 207) Skip(reader, 4 + 3);
        if (version >= 211)
        {
            int treeTopCount = ReadCount(reader.ReadInt32(), 1_000, "tree-top list");
            Skip(reader, treeTopCount * 4L);
        }
        if (version >= 212) Skip(reader, 2);
        if (version >= 216) Skip(reader, 4 * 4L);
        if (version >= 217) Skip(reader, 3);
        if (version >= 223) Skip(reader, 2);
        if (version >= 240) Skip(reader, 1);
        if (version >= 250) Skip(reader, 1);
        if (version >= 251) Skip(reader, 8);
        if (version >= 259) Skip(reader, 1);
        if (version >= 260) Skip(reader, 1);
        if (version >= 261) Skip(reader, 7);
        if (version >= 264) Skip(reader, 2);
        if (version >= 287) Skip(reader, 2);
        bool vampire = version >= 288 && reader.ReadBoolean();
        bool infected = version >= 296 && reader.ReadBoolean();
        if (version >= 291) Skip(reader, 8);

        bool teamSpawns = false;
        if (version >= 297)
        {
            teamSpawns = reader.ReadBoolean();
            int teamSpawnCount = reader.ReadByte();
            Skip(reader, teamSpawnCount * 4L);
        }

        bool dualDungeons = version >= 304 && reader.ReadBoolean();
        bool moreLightning = false;
        bool noLightning = false;
        if (version >= 323)
        {
            moreLightning = reader.ReadBoolean();
            noLightning = reader.ReadBoolean();
        }
        if (version >= 299 && version < 313) Skip(reader, 4);
        string manifest = version >= 299 ? reader.ReadString() : string.Empty;

        if (reader.BaseStream.Position > expectedEnd)
            throw new InvalidDataException($"World header overran section end {expectedEnd}.");
        reader.BaseStream.Position = expectedEnd;

        return new WorldMetadata(
            version, title, seed, worldId, width, height, gameMode,
            new TilePosition(spawnX, spawnY), worldSurface, rockLayer,
            new TilePosition(dungeonX, dungeonY), isCrimson, hardMode,
            drunk, forTheWorthy, celebration, theConstant, notTheBees,
            remix, noTraps, zenith, skyblock, vampire, infected,
            teamSpawns, dualDungeons, moreLightning, noLightning, manifest);
    }

    private static (WorldTileGrid Grid, Dictionary<ushort, long> TileCounts, Dictionary<ushort, long> WallCounts)
        ReadTiles(BinaryReader reader, int width, int height, uint version, bool[] frameImportant)
    {
        WorldTileGrid grid = new(width, height);
        Dictionary<ushort, long> tileCounts = [];
        Dictionary<ushort, long> wallCounts = [];

        for (int x = 0; x < width; x++)
        {
            int y = 0;
            while (y < height)
            {
                TileRun run = ReadTileRun(reader, version, frameImportant);
                int lastY = checked(y + run.Repetitions);
                if (lastY >= height)
                    throw new InvalidDataException($"Tile RLE exceeded column {x} at row {y}.");

                int runLength = run.Repetitions + 1;
                if (run.Active)
                    AddCount(tileCounts, run.Type, runLength);
                if (run.Wall != 0)
                    AddCount(wallCounts, run.Wall, runLength);

                byte wallClass = WallClasses.Classify(run.Wall);
                for (int tileY = y; tileY <= lastY; tileY++)
                {
                    int index = grid.Index(x, tileY);
                    if (run.Active) grid.Tiles[index] = run.Type;
                    grid.WallClasses[index] = wallClass;
                    grid.LiquidClasses[index] = run.LiquidClass;
                }

                if (run.Active && ShouldKeepFrame(run.Type))
                    grid.Frames[grid.Index(x, y)] = (run.FrameX, run.FrameY);
                y = lastY + 1;
            }
        }

        return (grid, tileCounts, wallCounts);
    }

    private static List<WorldChest> ReadChests(BinaryReader reader, uint version, WorldTileGrid grid)
    {
        int chestCount = ReadCount(reader.ReadInt16(), 8_000, "chest list");
        int legacySlots = version < 294 ? ReadCount(reader.ReadInt16(), 1_000, "chest slot count") : 0;
        List<WorldChest> chests = new(chestCount);
        for (int chestIndex = 0; chestIndex < chestCount; chestIndex++)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            string name = reader.ReadString();
            int slots = version >= 294 ? ReadCount(reader.ReadInt32(), 1_000, "chest size") : legacySlots;
            List<ChestItem> items = [];
            for (int slot = 0; slot < slots; slot++)
            {
                short stack = reader.ReadInt16();
                if (stack == 0) continue;
                int itemId = reader.ReadInt32();
                byte prefix = reader.ReadByte();
                items.Add(new ChestItem(itemId, stack < 0 ? 1 : stack, prefix));
            }

            ushort tileType = WorldTileGrid.Air;
            int style = -1;
            if (x >= 0 && x < grid.Width && y >= 0 && y < grid.Height)
            {
                int index = grid.Index(x, y);
                tileType = grid.Tiles[index];
                if (grid.Frames.TryGetValue(index, out (short FrameX, short FrameY) frame))
                    style = frame.FrameX >= 0 ? frame.FrameX / 36 : -1;
            }
            chests.Add(new WorldChest(chestIndex, new TilePosition(x, y), name, tileType, style, items));
        }
        return chests;
    }

    private static TileRun ReadTileRun(BinaryReader reader, uint version, bool[] frameImportant)
    {
        byte header1 = reader.ReadByte();
        byte header2 = 0;
        byte header3 = 0;
        if ((header1 & 0x01) != 0)
        {
            header2 = reader.ReadByte();
            if ((header2 & 0x01) != 0)
            {
                header3 = reader.ReadByte();
                if (version >= 269 && (header3 & 0x01) != 0)
                    _ = reader.ReadByte();
            }
        }

        bool active = (header1 & 0x02) != 0;
        ushort type = WorldTileGrid.Air;
        short frameX = -1;
        short frameY = -1;
        if (active)
        {
            type = (header1 & 0x20) == 0 ? reader.ReadByte() : reader.ReadUInt16();
            if (type >= frameImportant.Length || frameImportant[type])
            {
                frameX = reader.ReadInt16();
                frameY = reader.ReadInt16();
            }
            if ((header3 & 0x08) != 0) Skip(reader, 1);
        }

        ushort wall = 0;
        if ((header1 & 0x04) != 0)
        {
            wall = reader.ReadByte();
            if ((header3 & 0x10) != 0) Skip(reader, 1);
        }

        byte liquidClass = 0;
        byte liquidType = (byte)((header1 & 0x18) >> 3);
        if (liquidType != 0)
        {
            _ = reader.ReadByte();
            liquidClass = (header3 & 0x80) != 0
                ? (byte)4
                : liquidType switch { 2 => (byte)2, 3 => (byte)3, _ => (byte)1 };
        }

        if (version >= 222 && (header3 & 0x40) != 0)
            wall = (ushort)(wall | (reader.ReadByte() << 8));

        int repetitions = (header1 >> 6) switch
        {
            0 => 0,
            1 => reader.ReadByte(),
            _ => reader.ReadInt16()
        };
        if (repetitions < 0)
            throw new InvalidDataException("Negative tile RLE count.");

        return new TileRun(active, type, wall, liquidClass, frameX, frameY, repetitions);
    }

    private static bool ShouldKeepFrame(ushort type) =>
        TileIds.IsContainer(type) || type is 12 or 26 or 31 or 186 or 187 or 231;

    private static void AddCount(Dictionary<ushort, long> counts, ushort id, int amount) =>
        counts[id] = counts.GetValueOrDefault(id) + amount;

    private static bool[] ReadPackedBooleans(BinaryReader reader)
    {
        short count = reader.ReadInt16();
        if (count <= 0) throw new InvalidDataException("Invalid tile-frame bit count.");
        bool[] values = new bool[count];
        for (int i = 0; i < count; i += 8)
        {
            byte bits = reader.ReadByte();
            for (int bit = 0; bit < Math.Min(8, count - i); bit++)
                values[i + bit] = (bits & (1 << bit)) != 0;
        }
        return values;
    }

    private static void ValidateSections(int[] sections, long fileLength)
    {
        int previous = 0;
        foreach (int section in sections)
        {
            if (section <= previous || section > fileLength)
                throw new InvalidDataException($"Invalid section pointer {section}.");
            previous = section;
        }
    }

    private static int ReadCount(int value, int maximum, string description)
    {
        if (value < 0 || value > maximum)
            throw new InvalidDataException($"Invalid {description} count {value}.");
        return value;
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count) throw new EndOfStreamException();
        return data;
    }

    private static void Skip(BinaryReader reader, long count)
    {
        long position = checked(reader.BaseStream.Position + count);
        if (count < 0 || position > reader.BaseStream.Length) throw new EndOfStreamException();
        reader.BaseStream.Position = position;
    }

    private readonly record struct TileRun(
        bool Active,
        ushort Type,
        ushort Wall,
        byte LiquidClass,
        short FrameX,
        short FrameY,
        int Repetitions);
}
