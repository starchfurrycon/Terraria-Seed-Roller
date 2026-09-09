using System.Collections.ObjectModel;

namespace TerrariaSeedRoller.Core;

/// <summary>
/// Reads and analyses a vanilla world without modifying it. All calculations are
/// based on the final .wld produced by the installed Terraria version.
/// </summary>
public sealed class WorldAnalyzer
{
    private const int Scale = 8;
    private const double ProgressionLockPenalty = 1200d;
    private static readonly CellFlags[] TileClassifications = CreateTileClassifications();

    public WorldAnalysis Analyze(string worldPath, RollProfile? profile = null,
        CancellationToken cancellationToken = default)
    {
        ParsedWorld world = TerrariaWorldReader.Read(worldPath);
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, double> metrics = new(StringComparer.OrdinalIgnoreCase);
        foreach ((ushort id, long count) in world.TileCounts)
            metrics[MetricKeys.TileCount(id)] = count;
        foreach ((ushort id, long count) in world.WallCounts)
            metrics[MetricKeys.WallCount(id)] = count;

        CoarseWorld coarse = BuildCoarseWorld(world, cancellationToken);
        List<WorldRegion> regions = BuildRegions(coarse, cancellationToken);
        PopulateWorldMetrics(world, coarse, regions, metrics);
        PopulateSurfaceSpreadMetrics(world, metrics, cancellationToken);

        AccessCostMap accessCosts = CalculateAccessCosts(world, coarse, cancellationToken);
        PopulateAccessMetrics(accessCosts, metrics);
        List<ItemFinding> findings = BuildItemFindings(world, coarse, accessCosts.Cells, metrics);
        PopulateLootMetrics(world, findings, metrics);

        IReadOnlyDictionary<string, double> frozenMetrics =
            new ReadOnlyDictionary<string, double>(metrics);
        WorldEvaluation? evaluation = profile is null
            ? null
            : ProfileEvaluator.Evaluate(frozenMetrics, profile);

        return new WorldAnalysis
        {
            WorldPath = world.Path,
            Metadata = world.Metadata,
            Metrics = frozenMetrics,
            Regions = regions.AsReadOnly(),
            Chests = world.Chests,
            ImportantItems = findings.AsReadOnly(),
            Overview = coarse.Overview,
            OverviewWidth = coarse.Width,
            OverviewHeight = coarse.Height,
            Evaluation = evaluation
        };
    }

    private static CoarseWorld BuildCoarseWorld(ParsedWorld world, CancellationToken token)
    {
        int coarseWidth = (world.Metadata.Width + Scale - 1) / Scale;
        int coarseHeight = (world.Metadata.Height + Scale - 1) / Scale;
        CellFlags[] flags = new CellFlags[checked(coarseWidth * coarseHeight)];
        float[] movementCosts = new float[flags.Length];
        byte[] overview = new byte[flags.Length];
        WorldTileGrid grid = world.Grid;
        double nearestLifeCrystalSquared = double.PositiveInfinity;

        for (int cellX = 0; cellX < coarseWidth; cellX++)
        {
            if ((cellX & 31) == 0) token.ThrowIfCancellationRequested();
            int startX = cellX * Scale;
            int endX = Math.Min(startX + Scale, grid.Width);
            for (int cellY = 0; cellY < coarseHeight; cellY++)
            {
                int startY = cellY * Scale;
                int endY = Math.Min(startY + Scale, grid.Height);
                CellFlags cellFlags = CellFlags.None;
                int solid = 0;
                int hard = 0;
                int water = 0;
                int lava = 0;
                int shimmer = 0;
                int jungle = 0;
                int snow = 0;
                int desert = 0;
                int evil = 0;
                int dungeon = 0;
                int spider = 0;
                int hive = 0;
                int temple = 0;
                for (int x = startX; x < endX; x++)
                {
                    for (int y = startY; y < endY; y++)
                    {
                        int index = grid.Index(x, y);
                        ushort tile = grid.Tiles[index];
                        byte wall = grid.WallClasses[index];
                        byte liquid = grid.LiquidClasses[index];
                        if (tile != WorldTileGrid.Air)
                        {
                            CellFlags tileFlags = ClassifyTile(tile);
                            cellFlags |= tileFlags;
                            if (!TileIds.IsPassableDecoration(tile)) solid++;
                            if (TileIds.IsDungeonBrick(tile) || tile == TileIds.LihzahrdBrick ||
                                (tileFlags & (CellFlags.Corruption | CellFlags.Crimson)) != 0) hard++;
                            if ((tileFlags & CellFlags.Jungle) != 0) jungle++;
                            if ((tileFlags & CellFlags.Snow) != 0) snow++;
                            if ((tileFlags & CellFlags.Desert) != 0) desert++;
                            if ((tileFlags & (CellFlags.Corruption | CellFlags.Crimson)) != 0) evil++;
                            if ((tileFlags & CellFlags.Dungeon) != 0) dungeon++;
                            if ((tileFlags & CellFlags.Hive) != 0) hive++;
                            if ((tileFlags & CellFlags.Temple) != 0) temple++;
                            if (tile == TileIds.LifeCrystal)
                            {
                                double dx = x - world.Metadata.Spawn.X;
                                double dy = y - world.Metadata.Spawn.Y;
                                nearestLifeCrystalSquared = Math.Min(nearestLifeCrystalSquared,
                                    dx * dx + dy * dy);
                            }
                        }
                        CellFlags wallFlags = wall switch
                        {
                            WallClasses.Dungeon => CellFlags.Dungeon,
                            WallClasses.Spider => CellFlags.Spider,
                            WallClasses.Hive => CellFlags.Hive,
                            WallClasses.Temple => CellFlags.Temple,
                            WallClasses.Marble => CellFlags.Marble,
                            WallClasses.Granite => CellFlags.Granite,
                            _ => CellFlags.None
                        };
                        cellFlags |= wallFlags;
                        if ((wallFlags & CellFlags.Dungeon) != 0) dungeon++;
                        if ((wallFlags & CellFlags.Spider) != 0) spider++;
                        if ((wallFlags & CellFlags.Hive) != 0) hive++;
                        if ((wallFlags & CellFlags.Temple) != 0) temple++;
                        if (liquid == 1) water++;
                        else if (liquid == 2) lava++;
                        else if (liquid == 4) shimmer++;
                    }
                }

                if (shimmer > 0) cellFlags |= CellFlags.Shimmer;
                if (lava > 0) cellFlags |= CellFlags.Lava;
                int cellIndex = cellX * coarseHeight + cellY;
                flags[cellIndex] = cellFlags;
                overview[cellIndex] = OverviewClass(cellFlags);
                int area = Math.Max(1, (endX - startX) * (endY - startY));
                bool unsupportedSky = endY < world.Metadata.WorldSurface - 40 && solid == 0;
                float desertWeight = startY >= world.Metadata.WorldSurface ? 3f : 1.25f;
                movementCosts[cellIndex] = 1f +
                    4f * solid / area + 4f * hard / area +
                    2f * water / area + 15f * lava / area +
                    2.75f * jungle / area + 1f * snow / area +
                    desertWeight * desert / area + 2.5f * evil / area +
                    7f * dungeon / area + 2.5f * spider / area +
                    4f * hive / area + 12f * temple / area +
                    (unsupportedSky ? 1.5f : 0f);
            }
        }
        return new CoarseWorld(coarseWidth, coarseHeight, flags, movementCosts, overview,
            Math.Sqrt(nearestLifeCrystalSquared));
    }

    private static CellFlags ClassifyTile(ushort tile) =>
        tile < TileClassifications.Length ? TileClassifications[tile] : CellFlags.None;

    private static CellFlags[] CreateTileClassifications()
    {
        CellFlags[] result = new CellFlags[1024];
        for (ushort tile = 0; tile < result.Length; tile++)
        {
            CellFlags flags = CellFlags.None;
            if (TileIds.Corruption.Contains(tile)) flags |= CellFlags.Corruption;
            if (TileIds.Crimson.Contains(tile)) flags |= CellFlags.Crimson;
            if (TileIds.Jungle.Contains(tile)) flags |= CellFlags.Jungle;
            if (TileIds.Snow.Contains(tile)) flags |= CellFlags.Snow;
            if (TileIds.Desert.Contains(tile)) flags |= CellFlags.Desert;
            if (tile == TileIds.MushroomGrass || tile == TileIds.MushroomVines)
                flags |= CellFlags.Mushroom;
            if (TileIds.IsDungeonBrick(tile)) flags |= CellFlags.Dungeon;
            if (tile == TileIds.LihzahrdBrick) flags |= CellFlags.Temple;
            if (tile == TileIds.Hive) flags |= CellFlags.Hive;
            if (TileIds.IsMarble(tile)) flags |= CellFlags.Marble;
            if (TileIds.IsGranite(tile)) flags |= CellFlags.Granite;
            if (TileIds.IsCloud(tile)) flags |= CellFlags.FloatingIsland;
            if (TileIds.IsLivingTree(tile)) flags |= CellFlags.LivingTree;
            if (tile == TileIds.SandstoneBrick) flags |= CellFlags.Pyramid;
            if (tile == TileIds.MinecartTrack) flags |= CellFlags.Track;
            if (tile == TileIds.LifeCrystal) flags |= CellFlags.LifeCrystal;
            result[tile] = flags;
        }
        return result;
    }

    private static byte OverviewClass(CellFlags flags)
    {
        if ((flags & CellFlags.Shimmer) != 0) return 15;
        if ((flags & CellFlags.Crimson) != 0) return 2;
        if ((flags & CellFlags.Corruption) != 0) return 1;
        if ((flags & CellFlags.Temple) != 0) return 7;
        if ((flags & CellFlags.Dungeon) != 0) return 6;
        if ((flags & CellFlags.Hive) != 0) return 8;
        if ((flags & CellFlags.Spider) != 0) return 9;
        if ((flags & CellFlags.Mushroom) != 0) return 12;
        if ((flags & CellFlags.Jungle) != 0) return 3;
        if ((flags & CellFlags.Snow) != 0) return 4;
        if ((flags & CellFlags.Desert) != 0) return 5;
        if ((flags & CellFlags.FloatingIsland) != 0) return 13;
        if ((flags & CellFlags.LivingTree) != 0) return 14;
        if ((flags & CellFlags.Marble) != 0) return 10;
        if ((flags & CellFlags.Granite) != 0) return 11;
        if ((flags & CellFlags.Lava) != 0) return 16;
        if ((flags & CellFlags.Track) != 0) return 17;
        return 0;
    }

    private static List<WorldRegion> BuildRegions(CoarseWorld coarse, CancellationToken token)
    {
        List<WorldRegion> result = [];
        AddComponents(CellFlags.Corruption, RegionKind.Corruption, 2);
        AddComponents(CellFlags.Crimson, RegionKind.Crimson, 2);
        AddComponents(CellFlags.Jungle, RegionKind.Jungle, 10);
        AddComponents(CellFlags.Snow, RegionKind.Snow, 10);
        AddComponents(CellFlags.Desert, RegionKind.Desert, 8);
        AddComponents(CellFlags.Mushroom, RegionKind.GlowingMushroom, 2);
        AddComponents(CellFlags.Dungeon, RegionKind.Dungeon, 5);
        AddComponents(CellFlags.Temple, RegionKind.Temple, 4);
        AddComponents(CellFlags.Hive, RegionKind.Hive, 3);
        AddComponents(CellFlags.Marble, RegionKind.Marble, 3);
        AddComponents(CellFlags.Granite, RegionKind.Granite, 3);
        AddComponents(CellFlags.Spider, RegionKind.Spider, 2);
        AddComponents(CellFlags.FloatingIsland, RegionKind.FloatingIsland, 2);
        AddComponents(CellFlags.LivingTree, RegionKind.LivingTree, 3);
        AddComponents(CellFlags.Shimmer, RegionKind.Shimmer, 1);
        // A single vanilla sky island contains a main cloud mass plus several
        // detached decorative cloud puffs. Treating every puff as an island
        // substantially over-counts them (a normal small world can appear to
        // have 9-13 instead of 3-4). Generated island centres are kept well
        // apart, so merging nearby visible fragments reconstructs the actual
        // structures while still counting sky lakes, which have no chest.
        MergeNearbyRegions(RegionKind.FloatingIsland, 40, 40);
        // Leaves and roots can also be separated from the trunk by a coarse
        // cell boundary. Only bridge a much smaller gap for living trees.
        MergeNearbyRegions(RegionKind.LivingTree, 16, 16);
        return result;

        void AddComponents(CellFlags wanted, RegionKind kind, int minimumCells)
        {
            bool[] visited = new bool[coarse.Flags.Length];
            Queue<int> queue = new();
            for (int x = 0; x < coarse.Width; x++)
            {
                if ((x & 63) == 0) token.ThrowIfCancellationRequested();
                for (int y = 0; y < coarse.Height; y++)
                {
                    int start = coarse.Index(x, y);
                    if (visited[start] || (coarse.Flags[start] & wanted) == 0) continue;
                    visited[start] = true;
                    queue.Enqueue(start);
                    int minX = x, maxX = x, minY = y, maxY = y, count = 0;
                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue();
                        int cx = current / coarse.Height;
                        int cy = current % coarse.Height;
                        count++;
                        minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                        minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                        Visit(cx - 1, cy); Visit(cx + 1, cy);
                        Visit(cx, cy - 1); Visit(cx, cy + 1);
                        Visit(cx - 1, cy - 1); Visit(cx + 1, cy - 1);
                        Visit(cx - 1, cy + 1); Visit(cx + 1, cy + 1);
                    }
                    if (count >= minimumCells)
                    {
                        result.Add(new WorldRegion(kind, minX * Scale,
                            Math.Min((maxX + 1) * Scale - 1, coarse.Width * Scale - 1),
                            minY * Scale, Math.Min((maxY + 1) * Scale - 1, coarse.Height * Scale - 1),
                            count * Scale * Scale));
                    }

                    void Visit(int nx, int ny)
                    {
                        if ((uint)nx >= (uint)coarse.Width || (uint)ny >= (uint)coarse.Height) return;
                        int next = coarse.Index(nx, ny);
                        if (visited[next] || (coarse.Flags[next] & wanted) == 0) return;
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }
        }

        void MergeNearbyRegions(RegionKind kind, int maximumHorizontalGap,
            int maximumVerticalGap)
        {
            List<WorldRegion> candidates = result.Where(region => region.Kind == kind).ToList();
            if (candidates.Count < 2) return;

            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < candidates.Count && !changed; i++)
                {
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        WorldRegion first = candidates[i];
                        WorldRegion second = candidates[j];
                        int horizontalGap = AxisGap(first.MinX, first.MaxX, second.MinX, second.MaxX);
                        int verticalGap = AxisGap(first.MinY, first.MaxY, second.MinY, second.MaxY);
                        if (horizontalGap > maximumHorizontalGap || verticalGap > maximumVerticalGap)
                            continue;

                        candidates[i] = new WorldRegion(
                            kind,
                            Math.Min(first.MinX, second.MinX),
                            Math.Max(first.MaxX, second.MaxX),
                            Math.Min(first.MinY, second.MinY),
                            Math.Max(first.MaxY, second.MaxY),
                            first.TileCount + second.TileCount);
                        candidates.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);

            result.RemoveAll(region => region.Kind == kind);
            result.AddRange(candidates);
        }

        static int AxisGap(int firstMinimum, int firstMaximum, int secondMinimum,
            int secondMaximum) => Math.Max(0,
                Math.Max(firstMinimum - secondMaximum - 1, secondMinimum - firstMaximum - 1));
    }

    private static void PopulateWorldMetrics(ParsedWorld world, CoarseWorld coarse,
        IReadOnlyList<WorldRegion> regions, Dictionary<string, double> metrics)
    {
        WorldMetadata meta = world.Metadata;
        metrics[MetricKeys.WorldEvilType] = meta.IsCrimson ? 2 : 1;
        metrics[MetricKeys.DungeonSide] = Math.Sign(meta.Dungeon.X - meta.Spawn.X);
        WorldRegion[] evil = regions.Where(r => r.Kind is RegionKind.Corruption or RegionKind.Crimson).ToArray();
        metrics[MetricKeys.EvilRegionCount] = evil.Length;
        metrics[MetricKeys.EvilLargestWidthTiles] = evil.Length == 0 ? 0 : evil.Max(r => r.Width);
        metrics[MetricKeys.EvilLargestWidthPercent] = 100d * metrics[MetricKeys.EvilLargestWidthTiles] / meta.Width;
        metrics[MetricKeys.EvilTileCount] = world.TileCounts.Where(p => TileIds.Evil.Contains(p.Key)).Sum(p => (double)p.Value);
        metrics[MetricKeys.EvilNearestSpawnTiles] = NearestDistance(meta.Spawn, evil);
        metrics[MetricKeys.EvilJungleGapTiles] = RegionGap(evil, regions.Where(r => r.Kind == RegionKind.Jungle));
        metrics[MetricKeys.EvilDungeonGapTiles] = RegionGap(evil, regions.Where(r => r.Kind == RegionKind.Dungeon));
        metrics[MetricKeys.EvilIsolationShaftEstimate] = evil.Length * 2d *
            Math.Max(0, meta.Height - Math.Max(0, (int)meta.WorldSurface - 30));

        metrics[MetricKeys.ChestCount] = world.Chests.Count;
        metrics[MetricKeys.LifeCrystalCount] = Count(world, TileIds.LifeCrystal) / 4d;
        metrics[MetricKeys.DemonAltarCount] = Count(world, TileIds.DemonAltar) / 6d;
        metrics[MetricKeys.ShadowOrbCount] = Count(world, TileIds.ShadowOrbs) / 4d;
        metrics[MetricKeys.MinecartTrackTiles] = Count(world, TileIds.MinecartTrack);
        metrics[MetricKeys.TempleTiles] = Count(world, TileIds.LihzahrdBrick);
        metrics[MetricKeys.ShimmerLiquidTiles] = world.Grid.LiquidClasses.LongCount(v => v == 4);
        metrics[MetricKeys.HiveCount] = regions.Count(r => r.Kind == RegionKind.Hive);
        metrics[MetricKeys.MarbleCaveCount] = regions.Count(r => r.Kind == RegionKind.Marble);
        metrics[MetricKeys.GraniteCaveCount] = regions.Count(r => r.Kind == RegionKind.Granite);
        metrics[MetricKeys.SpiderCaveCount] = regions.Count(r => r.Kind == RegionKind.Spider);
        metrics[MetricKeys.MushroomBiomeCount] = regions.Count(r => r.Kind == RegionKind.GlowingMushroom);
        metrics[MetricKeys.FloatingIslandCount] = regions.Count(r => r.Kind == RegionKind.FloatingIsland);
        metrics[MetricKeys.LivingTreeCount] = regions.Count(r => r.Kind == RegionKind.LivingTree);
        metrics[MetricKeys.PyramidCount] = CountCoarseComponents(coarse, CellFlags.Pyramid, 10);
        metrics[MetricKeys.SwordShrineCount] = CountSwordShrines(world.Grid);
        metrics[MetricKeys.TrapCount] = Count(world, TileIds.Boulder) + Count(world, TileIds.Explosives) +
            Count(world, TileIds.GeyserTrap);
        metrics[MetricKeys.LavaCellCount] = coarse.Flags.Count(f => (f & CellFlags.Lava) != 0);
        metrics[MetricKeys.GemTileCount] = new[] { TileIds.Sapphire, TileIds.Ruby, TileIds.Emerald,
            TileIds.Topaz, TileIds.Amethyst, TileIds.Diamond }.Sum(id => Count(world, id));
        metrics[MetricKeys.OreTileCount] = new[] { TileIds.Copper, TileIds.Tin, TileIds.Iron, TileIds.Lead,
            TileIds.Silver, TileIds.Tungsten, TileIds.Gold, TileIds.Platinum, TileIds.Demonite,
            TileIds.Crimtane }.Sum(id => Count(world, id));
        metrics[MetricKeys.HellstoneCount] = Count(world, TileIds.Hellstone);
        metrics[MetricKeys.DesertFossilCount] = Count(world, TileIds.DesertFossil);
        metrics[MetricKeys.HasTin] = Count(world, TileIds.Tin) > Count(world, TileIds.Copper) ? 1 : 0;
        metrics[MetricKeys.HasLead] = Count(world, TileIds.Lead) > Count(world, TileIds.Iron) ? 1 : 0;
        metrics[MetricKeys.HasTungsten] = Count(world, TileIds.Tungsten) > Count(world, TileIds.Silver) ? 1 : 0;
        metrics[MetricKeys.HasPlatinum] = Count(world, TileIds.Platinum) > Count(world, TileIds.Gold) ? 1 : 0;

        WorldRegion[] jungle = regions.Where(r => r.Kind == RegionKind.Jungle).ToArray();
        WorldRegion[] snow = regions.Where(r => r.Kind == RegionKind.Snow).ToArray();
        WorldRegion[] desert = regions.Where(r => r.Kind == RegionKind.Desert).ToArray();
        WorldRegion[] shimmer = regions.Where(r => r.Kind == RegionKind.Shimmer).ToArray();
        metrics[MetricKeys.JungleSide] = jungle.Length == 0 ? 0 :
            Math.Sign(jungle.OrderByDescending(r => r.TileCount).First().Centre.X - meta.Spawn.X);
        metrics[MetricKeys.DungeonDistanceTiles] = Distance(meta.Spawn, meta.Dungeon);
        metrics[MetricKeys.JungleDistanceTiles] = NearestDistance(meta.Spawn, jungle);
        metrics[MetricKeys.SnowDistanceTiles] = NearestDistance(meta.Spawn, snow);
        metrics[MetricKeys.DesertDistanceTiles] = NearestDistance(meta.Spawn, desert);
        metrics[MetricKeys.ShimmerDistanceTiles] = NearestDistance(meta.Spawn, shimmer);
        metrics[MetricKeys.SpawnNearestChestTiles] = world.Chests.Count == 0 ? double.PositiveInfinity :
            world.Chests.Min(c => Distance(meta.Spawn, c.Position));
        metrics[MetricKeys.SpawnNearestLifeCrystalTiles] = coarse.NearestLifeCrystalDistance;
        metrics[MetricKeys.SpawnFlatness] = CalculateSpawnFlatness(world);
    }

    private static void PopulateSurfaceSpreadMetrics(ParsedWorld world, Dictionary<string, double> metrics,
        CancellationToken token)
    {
        const int thornBridge = 12;
        int width = world.Metadata.Width;
        int surface = (int)Math.Round(world.Metadata.WorldSurface);
        int minY = Math.Max(10, surface - 200);
        int maxY = Math.Min(world.Metadata.Height - 10, surface + 140);
        int bandHeight = maxY - minY + 1;
        int length = checked(width * bandHeight);
        byte[] eligibleKinds = new byte[length];
        byte[] sourceKinds = new byte[length];
        bool[] sourceColumns = new bool[width];

        int BandIndex(int x, int y) => x * bandHeight + (y - minY);
        for (int x = 10; x < width - 10; x++)
        {
            if ((x & 255) == 0) token.ThrowIfCancellationRequested();
            for (int y = minY; y <= maxY; y++)
            {
                ushort tile = world.Grid.TileAt(x, y);
                byte sourceKind = tile switch
                {
                    TileIds.CorruptGrass or TileIds.CrimsonGrass or
                    TileIds.CorruptThorns or TileIds.CrimsonThorns => 1,
                    TileIds.CorruptJungleGrass or TileIds.CrimsonJungleGrass => 2,
                    _ => 0
                };
                if (sourceKind != 0)
                {
                    sourceKinds[BandIndex(x, y)] |= sourceKind;
                    sourceColumns[x] = true;
                }
                if (!TileIds.PreHardmodeSurfaceSpreadable.Contains(tile)) continue;
                // Grass only occupies soil with an exposed neighbouring cell. A
                // sunflower specifically protects the blocks directly beneath it;
                // it is not treated as an infinite vertical barrier.
                if (y > 0 && world.Grid.TileAt(x, y - 1) == TileIds.Sunflower) continue;
                bool exposed = false;
                for (int dx = -1; dx <= 1 && !exposed; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        ushort neighbour = world.Grid.TileAt(x + dx, y + dy);
                        if (neighbour == WorldTileGrid.Air || TileIds.IsPassableDecoration(neighbour))
                        {
                            exposed = true;
                            break;
                        }
                    }
                if (exposed)
                {
                    bool jungleSoil = tile is TileIds.Mud or TileIds.JungleGrass or
                        TileIds.CorruptJungleGrass or TileIds.CrimsonJungleGrass;
                    eligibleKinds[BandIndex(x, y)] = jungleSoil ? (byte)2 : (byte)1;
                }
            }
        }

        byte[] airDistance = new byte[checked(length * 2)];
        Array.Fill(airDistance, byte.MaxValue);
        Queue<int> queue = new();
        for (int x = 10; x < width - 10; x++)
            for (int y = minY; y <= maxY; y++)
            {
                int index = BandIndex(x, y);
                byte kinds = sourceKinds[index];
                for (int layer = 0; layer < 2; layer++)
                {
                    byte kind = (byte)(1 << layer);
                    if ((kinds & kind) == 0) continue;
                    int state = layer * length + index;
                    airDistance[state] = 0;
                    queue.Enqueue(state);
                }
            }

        while (queue.Count > 0)
        {
            int state = queue.Dequeue();
            int layer = state / length;
            int current = state % length;
            byte kind = (byte)(1 << layer);
            int x = current / bandHeight;
            int y = current % bandHeight + minY;
            byte distanceInAir = airDistance[state];
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 10 || nx >= width - 10 || ny < minY || ny > maxY) continue;
                    int next = BandIndex(nx, ny);
                    byte candidate;
                    if ((eligibleKinds[next] & kind) != 0)
                    {
                        candidate = 0;
                    }
                    else
                    {
                        ushort tile = world.Grid.TileAt(nx, ny);
                        bool open = tile == WorldTileGrid.Air || TileIds.IsPassableDecoration(tile);
                        if (!open || ny >= surface || distanceInAir >= thornBridge) continue;
                        candidate = (byte)(distanceInAir + 1);
                    }
                    int nextState = layer * length + next;
                    if (candidate >= airDistance[nextState]) continue;
                    airDistance[nextState] = candidate;
                    queue.Enqueue(nextState);
                }
        }

        bool[] reachableColumns = new bool[width];
        for (int x = 10; x < width - 10; x++)
            for (int y = minY; y <= maxY; y++)
            {
                int index = BandIndex(x, y);
                if (((eligibleKinds[index] & 1) != 0 && airDistance[index] != byte.MaxValue) ||
                    ((eligibleKinds[index] & 2) != 0 && airDistance[length + index] != byte.MaxValue))
                    reachableColumns[x] = true;
            }

        List<(int Start, int End)> segments = [];
        int column = 0;
        while (column < width)
        {
            while (column < width && !reachableColumns[column]) column++;
            if (column >= width) break;
            int start = column, end = column, gap = 0;
            for (column++; column < width; column++)
            {
                if (reachableColumns[column]) { end = column; gap = 0; }
                else if (++gap > thornBridge) break;
            }
            segments.Add((start, end));
        }

        metrics[MetricKeys.EvilSurfaceRegionCount] = segments.Count;
        metrics[MetricKeys.EvilPreHardmodeClosureLargestWidth] = segments.Count == 0
            ? 0 : segments.Max(s => s.End - s.Start + 1);
        metrics[MetricKeys.EvilPreHardmodeClosureCombinedWidth] =
            segments.Sum(s => (double)(s.End - s.Start + 1));
        metrics[MetricKeys.EvilCombinedSurfaceWidthTiles] = sourceColumns.Count(v => v);
    }

    private static AccessCostMap CalculateAccessCosts(ParsedWorld world, CoarseWorld coarse,
        CancellationToken token)
    {
        HashSet<int> remainingTargets = world.Chests
            .Where(chest => chest.Items.Any(item => ImportantItemCatalog.ById.ContainsKey(item.ItemId)))
            .Select(chest => coarse.Index(
                Math.Clamp(chest.Position.X / Scale, 0, coarse.Width - 1),
                Math.Clamp(chest.Position.Y / Scale, 0, coarse.Height - 1)))
            .ToHashSet();
        CellFlags pendingDestinations = CellFlags.Dungeon | CellFlags.Jungle | CellFlags.Snow |
            CellFlags.Desert | CellFlags.Shimmer | CellFlags.Temple | CellFlags.LifeCrystal;
        int dungeonEntrance = coarse.Index(
            Math.Clamp(world.Metadata.Dungeon.X / Scale, 0, coarse.Width - 1),
            Math.Clamp(world.Metadata.Dungeon.Y / Scale, 0, coarse.Height - 1));
        Dictionary<CellFlags, double> destinationCosts = [];
        double[] distance = new double[coarse.Flags.Length];
        Array.Fill(distance, double.PositiveInfinity);
        int startX = Math.Clamp(world.Metadata.Spawn.X / Scale, 0, coarse.Width - 1);
        int startY = Math.Clamp(world.Metadata.Spawn.Y / Scale, 0, coarse.Height - 1);
        int start = coarse.Index(startX, startY);
        distance[start] = 0;
        PriorityQueue<int, double> queue = new();
        queue.Enqueue(start, 0);
        int visited = 0;
        while (queue.TryDequeue(out int current, out double priority))
        {
            if (priority != distance[current]) continue;
            remainingTargets.Remove(current);
            CellFlags reached = coarse.Flags[current] & pendingDestinations & ~CellFlags.Dungeon;
            if (current == dungeonEntrance && (pendingDestinations & CellFlags.Dungeon) != 0)
                reached |= CellFlags.Dungeon;
            if (reached != CellFlags.None)
            {
                foreach (CellFlags destination in DestinationFlags)
                {
                    if ((reached & destination) == 0) continue;
                    destinationCosts[destination] = priority;
                    pendingDestinations &= ~destination;
                }
            }
            if (remainingTargets.Count == 0 && pendingDestinations == CellFlags.None) break;
            if ((++visited & 8191) == 0) token.ThrowIfCancellationRequested();
            int x = current / coarse.Height;
            int y = current % coarse.Height;
            Relax(x - 1, y); Relax(x + 1, y); Relax(x, y - 1); Relax(x, y + 1);
            void Relax(int nx, int ny)
            {
                if ((uint)nx >= (uint)coarse.Width || (uint)ny >= (uint)coarse.Height) return;
                int next = coarse.Index(nx, ny);
                double directionMultiplier = ny == y ? 1d : 1.35d;
                double candidate = priority + (coarse.MovementCosts[current] +
                    coarse.MovementCosts[next]) * 4d * directionMultiplier;
                if (candidate >= distance[next]) return;
                distance[next] = candidate;
                queue.Enqueue(next, candidate);
            }
        }
        return new AccessCostMap(distance, destinationCosts);
    }

    private static void PopulateAccessMetrics(AccessCostMap costs, Dictionary<string, double> metrics)
    {
        metrics[MetricKeys.DungeonAccessCost] = costs.Get(CellFlags.Dungeon);
        metrics[MetricKeys.JungleAccessCost] = costs.Get(CellFlags.Jungle);
        metrics[MetricKeys.SnowAccessCost] = costs.Get(CellFlags.Snow);
        metrics[MetricKeys.DesertAccessCost] = costs.Get(CellFlags.Desert);
        metrics[MetricKeys.ShimmerAccessCost] = costs.Get(CellFlags.Shimmer);
        metrics[MetricKeys.TempleAccessCost] = costs.Get(CellFlags.Temple) + ProgressionLockPenalty;
        metrics[MetricKeys.SpawnNearestLifeCrystalAccessCost] = costs.Get(CellFlags.LifeCrystal);
    }

    private static List<ItemFinding> BuildItemFindings(ParsedWorld world, CoarseWorld coarse,
        double[] accessCosts, Dictionary<string, double> metrics)
    {
        List<ItemFinding> findings = [];
        foreach (WorldChest chest in world.Chests)
        {
            bool locked = IsProgressionLocked(chest);
            string kind = ChestKind(chest);
            int cx = Math.Clamp(chest.Position.X / Scale, 0, coarse.Width - 1);
            int cy = Math.Clamp(chest.Position.Y / Scale, 0, coarse.Height - 1);
            double access = accessCosts[coarse.Index(cx, cy)] +
                (locked ? ProgressionLockPenalty : 0d);
            foreach (ChestItem item in chest.Items)
            {
                if (!ImportantItemCatalog.ById.TryGetValue(item.ItemId, out ImportantItem? definition)) continue;
                findings.Add(new ItemFinding(item.ItemId, definition.ChineseName, chest.Position, item.Stack,
                    Distance(world.Metadata.Spawn, chest.Position), access, locked, kind));
            }
        }
        metrics[MetricKeys.ExtractinatorCount] = findings.Where(f => f.ItemId == 997).Sum(f => f.Stack);
        return findings;
    }

    private static void PopulateLootMetrics(ParsedWorld world, IReadOnlyList<ItemFinding> findings,
        Dictionary<string, double> metrics)
    {
        foreach (ImportantItem item in ImportantItemCatalog.All)
        {
            ItemFinding[] matches = findings.Where(f => f.ItemId == item.Id).ToArray();
            metrics[MetricKeys.ItemCount(item.Id)] = matches.Sum(f => (double)f.Stack);
            metrics[MetricKeys.ItemChestCount(item.Id)] = matches.Length;
            metrics[MetricKeys.ItemNearestTiles(item.Id)] = matches.Length == 0
                ? double.PositiveInfinity : matches.Min(f => f.DistanceFromSpawnTiles);
            metrics[MetricKeys.ItemAccessCost(item.Id)] = matches.Length == 0
                ? double.PositiveInfinity : matches.Min(f => f.EstimatedAccessCost);
        }
    }

    private static double CalculateSpawnFlatness(ParsedWorld world)
    {
        List<int> heights = [];
        int centre = world.Metadata.Spawn.X;
        int minY = Math.Max(10, (int)world.Metadata.WorldSurface - 120);
        int maxY = Math.Min(world.Metadata.Height - 10, (int)world.Metadata.WorldSurface + 160);
        for (int x = Math.Max(10, centre - 60); x <= Math.Min(world.Metadata.Width - 11, centre + 60); x += 3)
        {
            for (int y = minY; y <= maxY; y++)
            {
                ushort tile = world.Grid.TileAt(x, y);
                if (tile != WorldTileGrid.Air && !TileIds.IsPassableDecoration(tile))
                {
                    heights.Add(y);
                    break;
                }
            }
        }
        if (heights.Count < 2) return 0;
        double mean = heights.Average();
        double deviation = Math.Sqrt(heights.Sum(y => (y - mean) * (y - mean)) / heights.Count);
        return 100d / (1d + deviation);
    }

    private static int CountCoarseComponents(CoarseWorld coarse, CellFlags wanted, int minimumCells)
    {
        bool[] seen = new bool[coarse.Flags.Length];
        Queue<int> queue = new();
        int count = 0;
        for (int i = 0; i < seen.Length; i++)
        {
            if (seen[i] || (coarse.Flags[i] & wanted) == 0) continue;
            seen[i] = true; queue.Enqueue(i);
            int size = 0;
            while (queue.Count != 0)
            {
                int current = queue.Dequeue(); size++;
                int x = current / coarse.Height, y = current % coarse.Height;
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
                void Visit(int nx, int ny)
                {
                    if ((uint)nx >= (uint)coarse.Width || (uint)ny >= (uint)coarse.Height) return;
                    int next = coarse.Index(nx, ny);
                    if (seen[next] || (coarse.Flags[next] & wanted) == 0) return;
                    seen[next] = true; queue.Enqueue(next);
                }
            }
            if (size >= minimumCells) count++;
        }
        return count;
    }

    private static int CountSwordShrines(WorldTileGrid grid)
    {
        int count = 0;
        foreach ((int index, (short frameX, short frameY)) in grid.Frames)
        {
            // Tile 187 uses Style3x2: style 17 begins at frameX 17 * 54.
            // Count only the object's top-left tile, not all six frame pieces.
            if (grid.Tiles[index] == 187 && frameX == 17 * 54 && frameY == 0) count++;
        }
        return count;
    }

    private static bool IsProgressionLocked(WorldChest chest) =>
        chest.TileType == TileIds.Containers &&
            (chest.Style is 2 or 4 or 23 or 24 or 25 or 26 or 27 or 36 or 38 or 40) ||
        chest.TileType == TileIds.Containers2 && chest.Style == 13;

    private static string ChestKind(WorldChest chest) => (chest.TileType, chest.Style) switch
    {
        (TileIds.Containers, 0) => "木箱",
        (TileIds.Containers, 1) => "金箱",
        (TileIds.Containers, 2) => "锁金箱",
        (TileIds.Containers, 3) => "暗影箱",
        (TileIds.Containers, 4) => "暗影箱",
        (TileIds.Containers, 5) => "桶",
        (TileIds.Containers, 6) => "垃圾桶",
        (TileIds.Containers, 7) => "黑檀木箱",
        (TileIds.Containers, 8) => "红木箱",
        (TileIds.Containers, 9) => "珍珠木箱",
        (TileIds.Containers, 10) => "常春藤箱",
        (TileIds.Containers, 11) => "冰冻箱",
        (TileIds.Containers, 12) => "生命木箱",
        (TileIds.Containers, 13) => "天域箱",
        (TileIds.Containers, 14) => "阴影木箱",
        (TileIds.Containers, 15) => "蛛丝箱",
        (TileIds.Containers, 16) => "丛林蜥蜴箱",
        (TileIds.Containers, 17) => "水中箱",
        (TileIds.Containers, 18 or 23) => "丛林箱",
        (TileIds.Containers, 19 or 24) => "腐化箱",
        (TileIds.Containers, 20 or 25) => "猩红箱",
        (TileIds.Containers, 21 or 26) => "神圣箱",
        (TileIds.Containers, 22 or 27) => "冰雪箱",
        (TileIds.Containers, 28) => "王朝箱",
        (TileIds.Containers, 29) => "蜂蜜箱",
        (TileIds.Containers, 30) => "蒸汽朋克箱",
        (TileIds.Containers, 31) => "棕榈木箱",
        (TileIds.Containers, 32) => "蘑菇箱",
        (TileIds.Containers, 33) => "针叶木箱",
        (TileIds.Containers, 34) => "史莱姆箱",
        (TileIds.Containers, 35 or 36) => "绿色地牢箱",
        (TileIds.Containers, 37 or 38) => "粉色地牢箱",
        (TileIds.Containers, 39 or 40) => "蓝色地牢箱",
        (TileIds.Containers, 41) => "骨箱",
        (TileIds.Containers, 42) => "仙人掌箱",
        (TileIds.Containers, 43) => "血肉箱",
        (TileIds.Containers, 44) => "黑曜石箱",
        (TileIds.Containers, 45) => "南瓜箱",
        (TileIds.Containers, 46) => "阴森木箱",
        (TileIds.Containers, 47) => "玻璃箱",
        (TileIds.Containers, 48) => "火星箱",
        (TileIds.Containers, 49) => "陨石箱",
        (TileIds.Containers, 50) => "花岗岩箱",
        (TileIds.Containers, 51) => "大理石箱",
        (TileIds.Containers2, 0) => "水晶箱",
        (TileIds.Containers2, 1) => "金制箱",
        (TileIds.Containers2, 2) => "蜘蛛箱",
        (TileIds.Containers2, 3) => "病变箱",
        (TileIds.Containers2, 4) => "死人宝箱",
        (TileIds.Containers2, 5) => "日耀箱",
        (TileIds.Containers2, 6) => "星旋箱",
        (TileIds.Containers2, 7) => "星云箱",
        (TileIds.Containers2, 8) => "星尘箱",
        (TileIds.Containers2, 9) => "高尔夫箱",
        (TileIds.Containers2, 10) => "沙漠箱",
        (TileIds.Containers2, 11) => "竹箱",
        (TileIds.Containers2, 12 or 13) => "地牢沙漠箱",
        (TileIds.Containers2, 14) => "珊瑚箱",
        (TileIds.Containers2, 15) => "气球箱",
        (TileIds.Containers2, 16) => "灰烬木箱",
        (TileIds.Containers2, 17) => "以太箱",
        (TileIds.Containers2, 18) => "坠星箱",
        (TileIds.Containers2, 19) => "妖精木箱",
        (TileIds.Containers2, 20) => "神圣家具箱",
        (TileIds.Containers2, 21) => "哥特箱",
        (TileIds.Containers2, 22) => "魔矿箱",
        (TileIds.Containers2, 23) => "猩红矿箱",
        (TileIds.Containers2, 24) => "雪箱",
        (TileIds.Containers2, 25) => "小雪怪毛皮箱",
        (TileIds.Containers2, 26) => "松木箱",
        (TileIds.Containers2, 27) => "复活节箱",
        (TileIds.Containers2, 28) => "石箱",
        (TileIds.Containers2, 29) => "水母箱",
        (TileIds.Containers2, 30) => "鸟妖箱",
        (TileIds.Containers2, 31) => "云箱",
        (TileIds.Containers2, 32) => "月辉板箱",
        (TileIds.Containers2, 33) => "图书管理员箱",
        (TileIds.Containers2, 34) => "尖刺箱",
        (TileIds.Containers2, 35) => "办公箱",
        (TileIds.Containers2, 36) => "禁戒箱",
        (TileIds.Containers2, 37) => "巨石箱",
        _ => $"容器（物块 {chest.TileType}，样式 {chest.Style}）"
    };

    private static double NearestDistance(TilePosition point, IEnumerable<WorldRegion> regions)
    {
        double nearest = double.PositiveInfinity;
        foreach (WorldRegion region in regions)
        {
            int x = Math.Clamp(point.X, region.MinX, region.MaxX);
            int y = Math.Clamp(point.Y, region.MinY, region.MaxY);
            nearest = Math.Min(nearest, Distance(point, new TilePosition(x, y)));
        }
        return nearest;
    }

    private static double RegionGap(IEnumerable<WorldRegion> left, IEnumerable<WorldRegion> right)
    {
        WorldRegion[] a = left.ToArray(), b = right.ToArray();
        if (a.Length == 0 || b.Length == 0) return double.PositiveInfinity;
        double nearest = double.PositiveInfinity;
        foreach (WorldRegion first in a)
            foreach (WorldRegion second in b)
            {
                int dx = Math.Max(0, Math.Max(first.MinX - second.MaxX, second.MinX - first.MaxX));
                int dy = Math.Max(0, Math.Max(first.MinY - second.MaxY, second.MinY - first.MaxY));
                nearest = Math.Min(nearest, Math.Sqrt((double)dx * dx + (double)dy * dy));
            }
        return nearest;
    }

    private static double Distance(TilePosition a, TilePosition b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Count(ParsedWorld world, ushort id) => world.TileCounts.GetValueOrDefault(id);

    [Flags]
    private enum CellFlags : uint
    {
        None = 0, Corruption = 1 << 0, Crimson = 1 << 1, Jungle = 1 << 2,
        Snow = 1 << 3, Desert = 1 << 4, Mushroom = 1 << 5, Dungeon = 1 << 6,
        Temple = 1 << 7, Hive = 1 << 8, Marble = 1 << 9, Granite = 1 << 10,
        Spider = 1 << 11, FloatingIsland = 1 << 12, LivingTree = 1 << 13,
        Shimmer = 1 << 14, Pyramid = 1 << 15, Lava = 1 << 16, Track = 1 << 17,
        LifeCrystal = 1 << 18
    }

    private static readonly CellFlags[] DestinationFlags =
    [
        CellFlags.Dungeon, CellFlags.Jungle, CellFlags.Snow, CellFlags.Desert,
        CellFlags.Shimmer, CellFlags.Temple, CellFlags.LifeCrystal
    ];

    private sealed record CoarseWorld(int Width, int Height, CellFlags[] Flags,
        float[] MovementCosts, byte[] Overview, double NearestLifeCrystalDistance)
    {
        public int Index(int x, int y) => x * Height + y;
    }

    private sealed record AccessCostMap(double[] Cells,
        IReadOnlyDictionary<CellFlags, double> Destinations)
    {
        public double Get(CellFlags destination) =>
            Destinations.GetValueOrDefault(destination, double.PositiveInfinity);
    }
}
