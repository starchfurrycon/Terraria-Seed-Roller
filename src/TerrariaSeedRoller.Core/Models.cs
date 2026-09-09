using System.Collections.ObjectModel;

namespace TerrariaSeedRoller.Core;

public enum WorldSize
{
    Small = 1,
    Medium = 2,
    Large = 3
}

public enum WorldDifficulty
{
    Classic = 1,
    Expert = 2,
    Master = 3,
    Journey = 4
}

public enum WorldEvil
{
    Random = 0,
    Corruption = 1,
    Crimson = 2
}

[Flags]
public enum SpecialSeedFlags
{
    None = 0,
    NotTheBees = 1,
    Drunk = 2,
    Celebration = 4,
    TheConstant = 8,
    ForTheWorthy = 16,
    NoTraps = 32,
    Remix = 64,
    Zenith = 128,
    Skyblock = 256
}

public enum CriterionKind
{
    Hard,
    Weighted
}

public enum MetricComparison
{
    AtLeast,
    AtMost,
    Equal,
    NotEqual,
    Between
}

public sealed record SeedRange(int Start, int End, bool RandomOrder = false)
{
    public IEnumerable<int> Enumerate(int? randomSeed = null)
    {
        if (End < Start)
        {
            throw new ArgumentOutOfRangeException(nameof(End), "Seed range end must not be less than start.");
        }

        long count = (long)End - Start + 1L;
        if (!RandomOrder)
        {
            for (int value = Start; value <= End; value++)
            {
                yield return value;
                if (value == int.MaxValue)
                {
                    break;
                }
            }
            yield break;
        }

        // A memory-free full-cycle permutation. An odd increment relatively prime to 2^32
        // visits each value in the selected interval once after modulo reduction is rejected.
        Random random = randomSeed.HasValue ? new Random(randomSeed.Value) : Random.Shared;
        long offset = random.NextInt64(count);
        long step = count == 1 ? 1 : PickCoprimeStep(count, random);
        long current = offset;
        for (long i = 0; i < count; i++)
        {
            yield return checked((int)((long)Start + current));
            // Addition with modular reduction avoids overflowing i * step for the
            // full signed 32-bit seed range.
            current = AddModulo(current, step, count);
        }
    }

    private static long AddModulo(long left, long right, long modulus) =>
        left >= modulus - right ? left - (modulus - right) : left + right;

    private static long PickCoprimeStep(long modulus, Random random)
    {
        long candidate;
        do
        {
            candidate = random.NextInt64(1, modulus);
        }
        while (GreatestCommonDivisor(candidate, modulus) != 1);
        return candidate;
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return Math.Abs(a);
    }
}

public sealed record GenerationSettings
{
    public required string TerrariaServerPath { get; init; }
    public required string CandidateDirectory { get; init; }
    public WorldSize Size { get; init; } = WorldSize.Small;
    public WorldDifficulty Difficulty { get; init; } = WorldDifficulty.Classic;
    public WorldEvil Evil { get; init; } = WorldEvil.Random;
    public SpecialSeedFlags SpecialSeeds { get; init; } = SpecialSeedFlags.None;
    public SeedRange Seeds { get; init; } = new(0, int.MaxValue, true);
    public int MaximumAttempts { get; init; } = 100;
    public int WinnersToKeep { get; init; } = 10;
    public int Parallelism { get; init; } = 1;
    public TimeSpan PerWorldTimeout { get; init; } = TimeSpan.FromMinutes(8);
    public bool KeepRejectedWorlds { get; init; }
    public int TopResultsToTrack { get; init; } = 50;
    public double MinimumFreeDiskGb { get; init; } = 2;

    public void Validate()
    {
        if (!File.Exists(TerrariaServerPath))
            throw new FileNotFoundException("TerrariaServer.exe was not found.", TerrariaServerPath);
        if (MaximumAttempts is < 1 or > 10_000_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
        if (WinnersToKeep is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(WinnersToKeep));
        if (Parallelism is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(Parallelism), "Parallelism must be between 1 and 8.");
        if (PerWorldTimeout < TimeSpan.FromSeconds(30) || PerWorldTimeout > TimeSpan.FromHours(2))
            throw new ArgumentOutOfRangeException(nameof(PerWorldTimeout));
        if (TopResultsToTrack < WinnersToKeep || TopResultsToTrack > 100_000)
            throw new ArgumentOutOfRangeException(nameof(TopResultsToTrack));
        if (MinimumFreeDiskGb is < 0.25 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(MinimumFreeDiskGb));
    }

    public string BuildCopiedSeed(int seed)
    {
        int evilValue = Evil switch
        {
            WorldEvil.Corruption => 1,
            WorldEvil.Crimson => 2,
            _ => 1 + Math.Abs(seed % 2)
        };

        return SpecialSeeds == SpecialSeedFlags.None
            ? $"{(int)Size}.{(int)Difficulty}.{evilValue}.{seed}"
            : $"{(int)Size}.{(int)Difficulty}.{evilValue}.{(int)SpecialSeeds}.{seed}";
    }
}

public sealed record CriterionDefinition
{
    public bool Enabled { get; init; } = true;
    public required string MetricKey { get; init; }
    public CriterionKind Kind { get; init; } = CriterionKind.Hard;
    public MetricComparison Comparison { get; init; } = MetricComparison.AtMost;
    public double Value { get; init; }
    public double SecondValue { get; init; }
    public double Weight { get; init; } = 1;
    public string? Label { get; init; }
}

public sealed record RollProfile
{
    public string Name { get; init; } = "Custom";
    public string Description { get; init; } = string.Empty;
    public List<CriterionDefinition> Criteria { get; init; } = [];
}

public sealed record RollConfiguration
{
    public required GenerationSettings Generation { get; init; }
    public required RollProfile Profile { get; init; }
}

public readonly record struct TilePosition(int X, int Y);

public sealed record WorldMetadata(
    uint FileVersion,
    string Title,
    string SeedText,
    int WorldId,
    int Width,
    int Height,
    int GameMode,
    TilePosition Spawn,
    double WorldSurface,
    double RockLayer,
    TilePosition Dungeon,
    bool IsCrimson,
    bool HardMode,
    bool Drunk,
    bool ForTheWorthy,
    bool Celebration,
    bool TheConstant,
    bool NotTheBees,
    bool Remix,
    bool NoTraps,
    bool Zenith,
    bool Skyblock,
    bool Vampire,
    bool Infected,
    bool TeamSpawns,
    bool DualDungeons,
    bool MoreLightning,
    bool NoLightning,
    string Manifest);

public sealed record ChestItem(int ItemId, int Stack, byte Prefix);

public sealed record WorldChest(
    int Index,
    TilePosition Position,
    string Name,
    ushort TileType,
    int Style,
    IReadOnlyList<ChestItem> Items);

public enum RegionKind
{
    Corruption,
    Crimson,
    Jungle,
    Snow,
    Desert,
    GlowingMushroom,
    Dungeon,
    Temple,
    Hive,
    Marble,
    Granite,
    Spider,
    FloatingIsland,
    LivingTree,
    Shimmer
}

public sealed record WorldRegion(
    RegionKind Kind,
    int MinX,
    int MaxX,
    int MinY,
    int MaxY,
    int TileCount)
{
    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public TilePosition Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);
}

public sealed record ItemFinding(
    int ItemId,
    string Name,
    TilePosition Position,
    int Stack,
    double DistanceFromSpawnTiles,
    double EstimatedAccessCost,
    bool ProgressionLocked,
    string ChestKind);

public sealed record CriterionResult(
    CriterionDefinition Criterion,
    bool Passed,
    double Actual,
    double ScoreContribution,
    string Message);

public sealed record WorldEvaluation(
    bool PassedHardCriteria,
    double Score,
    IReadOnlyList<CriterionResult> Criteria);

public sealed record WorldAnalysis
{
    public required string WorldPath { get; init; }
    public required WorldMetadata Metadata { get; init; }
    public required IReadOnlyDictionary<string, double> Metrics { get; init; }
    public required IReadOnlyList<WorldRegion> Regions { get; init; }
    public required IReadOnlyList<WorldChest> Chests { get; init; }
    public required IReadOnlyList<ItemFinding> ImportantItems { get; init; }
    public required byte[] Overview { get; init; }
    public required int OverviewWidth { get; init; }
    public required int OverviewHeight { get; init; }
    public WorldEvaluation? Evaluation { get; init; }
}

public sealed record RollResult(
    int Seed,
    string CopiedSeed,
    string WorldPath,
    TimeSpan GenerationDuration,
    TimeSpan AnalysisDuration,
    WorldAnalysis Analysis,
    string? Error = null);

public sealed record RollSessionResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Attempted,
    int Completed,
    int Failed,
    bool Cancelled,
    IReadOnlyList<RollResult> Winners,
    string OutputDirectory);

public sealed record RollProgress(
    int Attempted,
    int Completed,
    int Accepted,
    int Failed,
    int MaximumAttempts,
    int CurrentSeed,
    string Stage,
    TimeSpan Elapsed,
    double? LatestScore = null,
    string? Message = null);

public static class MetricKeys
{
    public const string WorldEvilType = "world.evilType";
    public const string DungeonSide = "world.dungeonSide";
    public const string JungleSide = "world.jungleSide";
    public const string HasTin = "world.ore.hasTin";
    public const string HasLead = "world.ore.hasLead";
    public const string HasTungsten = "world.ore.hasTungsten";
    public const string HasPlatinum = "world.ore.hasPlatinum";
    public const string EvilRegionCount = "evil.regions.count";
    public const string EvilLargestWidthTiles = "evil.regions.largestWidthTiles";
    public const string EvilLargestWidthPercent = "evil.regions.largestWidthPercent";
    public const string EvilCombinedSurfaceWidthTiles = "evil.surface.combinedWidthTiles";
    public const string EvilTileCount = "evil.tiles.count";
    public const string EvilNearestSpawnTiles = "evil.nearestSpawnTiles";
    public const string EvilJungleGapTiles = "evil.jungleGapTiles";
    public const string EvilDungeonGapTiles = "evil.dungeonGapTiles";
    public const string EvilIsolationShaftEstimate = "evil.isolation.estimatedShaftTiles";
    public const string EvilPreHardmodeClosureLargestWidth = "evil.prehardmode.closureLargestWidthTiles";
    public const string EvilPreHardmodeClosureCombinedWidth = "evil.prehardmode.closureCombinedWidthTiles";
    public const string EvilSurfaceRegionCount = "evil.surface.regions.count";
    public const string SpawnFlatness = "spawn.flatness";
    public const string SpawnNearestChestTiles = "spawn.nearestChestTiles";
    public const string SpawnNearestLifeCrystalTiles = "spawn.nearestLifeCrystalTiles";
    public const string DungeonDistanceTiles = "travel.dungeonDistanceTiles";
    public const string JungleDistanceTiles = "travel.jungleDistanceTiles";
    public const string SnowDistanceTiles = "travel.snowDistanceTiles";
    public const string DesertDistanceTiles = "travel.desertDistanceTiles";
    public const string ShimmerDistanceTiles = "travel.shimmerDistanceTiles";
    public const string ChestCount = "chests.count";
    public const string LifeCrystalCount = "resources.lifeCrystals";
    public const string DemonAltarCount = "resources.altars";
    public const string ShadowOrbCount = "resources.shadowOrbs";
    public const string MinecartTrackTiles = "structures.minecartTrackTiles";
    public const string PyramidCount = "structures.pyramids";
    public const string SwordShrineCount = "structures.swordShrines";
    public const string LivingTreeCount = "structures.livingTrees";
    public const string FloatingIslandCount = "structures.floatingIslands";
    public const string HiveCount = "structures.hives";
    public const string MarbleCaveCount = "structures.marbleCaves";
    public const string GraniteCaveCount = "structures.graniteCaves";
    public const string SpiderCaveCount = "structures.spiderCaves";
    public const string MushroomBiomeCount = "structures.mushroomBiomes";
    public const string ShimmerLiquidTiles = "resources.shimmerLiquidTiles";
    public const string TempleTiles = "structures.templeTiles";
    public const string TrapCount = "hazards.traps";
    public const string LavaCellCount = "hazards.lavaCells";
    public const string GemTileCount = "resources.gemTiles";
    public const string OreTileCount = "resources.prehardmodeOreTiles";
    public const string HellstoneCount = "resources.hellstoneTiles";
    public const string ExtractinatorCount = "loot.extractinators";
    public const string DesertFossilCount = "resources.desertFossilTiles";

    public static string TileCount(ushort tileId) => $"tiles.{tileId}.count";
    public static string WallCount(ushort wallId) => $"walls.{wallId}.count";
    public static string ItemCount(int itemId) => $"loot.item.{itemId}.count";
    public static string ItemChestCount(int itemId) => $"loot.item.{itemId}.chests";
    public static string ItemNearestTiles(int itemId) => $"loot.item.{itemId}.nearestTiles";
    public static string ItemAccessCost(int itemId) => $"loot.item.{itemId}.accessCost";
}

internal sealed class WorldTileGrid
{
    public const ushort Air = ushort.MaxValue;

    public WorldTileGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = GC.AllocateUninitializedArray<ushort>(checked(width * height));
        Array.Fill(Tiles, Air);
        WallClasses = new byte[checked(width * height)];
        LiquidClasses = new byte[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }
    public ushort[] Tiles { get; }
    public byte[] WallClasses { get; }
    public byte[] LiquidClasses { get; }
    public Dictionary<int, (short FrameX, short FrameY)> Frames { get; } = [];

    public int Index(int x, int y) => checked(x * Height + y);
    public ushort TileAt(int x, int y) => Tiles[Index(x, y)];
    public byte WallClassAt(int x, int y) => WallClasses[Index(x, y)];
    public byte LiquidClassAt(int x, int y) => LiquidClasses[Index(x, y)];
}

internal sealed record ParsedWorld(
    string Path,
    WorldMetadata Metadata,
    WorldTileGrid Grid,
    IReadOnlyList<WorldChest> Chests,
    IReadOnlyDictionary<ushort, long> TileCounts,
    IReadOnlyDictionary<ushort, long> WallCounts,
    long TileSectionStart,
    long TileSectionEnd,
    long ChestSectionEnd);

public static class ReadOnlyCollections
{
    internal static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(Dictionary<TKey, TValue> source)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(source);
}
