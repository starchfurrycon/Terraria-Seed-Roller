namespace TerrariaSeedRoller.Core;

public enum MetricPreference
{
    SmallerIsBetter,
    LargerIsBetter,
    TargetOnly
}

public sealed record MetricDefinition(
    string Key,
    string ChineseName,
    string EnglishName,
    string Unit,
    MetricPreference Preference,
    string Category,
    string Description);

public static class MetricCatalog
{
    private static readonly IReadOnlyList<MetricDefinition> Definitions = Build();
    private static readonly IReadOnlyDictionary<string, MetricDefinition> ByKeyMap =
        Definitions.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MetricDefinition> All => Definitions;
    public static IReadOnlyDictionary<string, MetricDefinition> ByKey => ByKeyMap;

    private static IReadOnlyList<MetricDefinition> Build()
    {
        List<MetricDefinition> result =
        [
            D(MetricKeys.WorldEvilType, "世界邪恶类型", "World evil type", "1腐化/2猩红", false, "世界变体", "可用 Equal 1/2 强制筛选腐化或猩红；通常直接在生成设置中选择更快。"),
            D(MetricKeys.DungeonSide, "地牢方向", "Dungeon side", "-1左/1右", false, "世界变体", "相对出生点的方向。"),
            D(MetricKeys.JungleSide, "丛林方向", "Jungle side", "-1左/1右", false, "世界变体", "最大丛林区域相对出生点的方向。"),
            D(MetricKeys.HasTin, "锡矿世界", "Tin world", "0否/1是", false, "世界变体", "第一矿级为锡而非铜。"),
            D(MetricKeys.HasLead, "铅矿世界", "Lead world", "0否/1是", false, "世界变体", "第二矿级为铅而非铁。"),
            D(MetricKeys.HasTungsten, "钨矿世界", "Tungsten world", "0否/1是", false, "世界变体", "第三矿级为钨而非银。"),
            D(MetricKeys.HasPlatinum, "铂金世界", "Platinum world", "0否/1是", false, "世界变体", "第四矿级为铂金而非金。"),
            D(MetricKeys.EvilRegionCount, "邪恶区域数", "Evil regions", "个", false, "邪恶控制", "实际邪恶物块的相邻区域数（8×8 采样连通）。"),
            D(MetricKeys.EvilLargestWidthTiles, "最大邪恶区宽度", "Largest evil width", "格", false, "邪恶控制", "当前肉前邪恶区域的最大水平跨度。"),
            D(MetricKeys.EvilLargestWidthPercent, "最大邪恶区占世界", "Largest evil width", "%", false, "邪恶控制", "最大邪恶区域宽度占世界宽度。"),
            D(MetricKeys.EvilPreHardmodeClosureLargestWidth, "肉前自由蔓延最大宽度", "Pre-Hardmode spread closure", "格", false, "邪恶控制", "沿实际暴露土壤逐格传播并保守考虑荆棘跨越空隙的闭包。"),
            D(MetricKeys.EvilPreHardmodeClosureCombinedWidth, "肉前自由蔓延合计宽度", "Combined spread closure", "格", false, "邪恶控制", "所有含邪恶源地表传播走廊的合计宽度。"),
            D(MetricKeys.EvilNearestSpawnTiles, "邪恶离出生点", "Evil distance from spawn", "格", true, "邪恶控制", "出生点到最近邪恶区域的直线距离。"),
            D(MetricKeys.EvilJungleGapTiles, "邪恶与丛林间距", "Evil-jungle gap", "格", true, "邪恶控制", "邪恶区域与丛林区域的最近间距。"),
            D(MetricKeys.EvilDungeonGapTiles, "邪恶与地牢间距", "Evil-dungeon gap", "格", true, "邪恶控制", "邪恶区域与地牢的最近间距。"),
            D(MetricKeys.EvilIsolationShaftEstimate, "隔离竖井工作量估算", "Isolation shaft estimate", "格", false, "邪恶控制", "按每个邪恶区两侧从地表向下估算；用于相对排序。"),
            D(MetricKeys.SpawnFlatness, "出生点平整度", "Spawn flatness", "分", true, "出生点", "出生点附近地面高度标准差换算的 0–100 分。"),
            D(MetricKeys.SpawnNearestChestTiles, "最近宝箱距离", "Nearest chest", "格", false, "出生点", "出生点到最近宝箱的直线距离。"),
            D(MetricKeys.SpawnNearestLifeCrystalTiles, "最近生命水晶距离", "Nearest life crystal", "格", false, "出生点", "出生点到最近生命水晶的直线距离。"),
            D(MetricKeys.DungeonDistanceTiles, "地牢距离", "Dungeon distance", "格", false, "行程", "出生点到地牢入口坐标的距离。"),
            D(MetricKeys.JungleDistanceTiles, "丛林距离", "Jungle distance", "格", false, "行程", "出生点到最近丛林区域的距离。"),
            D(MetricKeys.SnowDistanceTiles, "雪原距离", "Snow distance", "格", false, "行程", "出生点到最近雪原区域的距离。"),
            D(MetricKeys.DesertDistanceTiles, "沙漠距离", "Desert distance", "格", false, "行程", "出生点到最近沙漠区域的距离。"),
            D(MetricKeys.ShimmerDistanceTiles, "微光距离", "Shimmer distance", "格", false, "行程", "出生点到微光液体区域的距离。"),
            D(MetricKeys.ChestCount, "宝箱总数", "Chest count", "个", true, "资源", "世界文件宝箱表中的箱子数。"),
            D(MetricKeys.LifeCrystalCount, "生命水晶", "Life crystals", "个", true, "资源", "按物块尺寸折算的生命水晶数。"),
            D(MetricKeys.DemonAltarCount, "祭坛", "Altars", "个", true, "资源", "按物块尺寸折算的恶魔/猩红祭坛数。"),
            D(MetricKeys.ShadowOrbCount, "暗影珠/猩红之心", "Shadow orbs/hearts", "个", true, "资源", "按物块尺寸折算。"),
            D(MetricKeys.OreTileCount, "肉前矿物", "Pre-Hardmode ore", "物块", true, "资源", "铜至金/铂及邪恶矿的总物块数。"),
            D(MetricKeys.GemTileCount, "宝石矿", "Gem tiles", "物块", true, "资源", "六种天然宝石物块总数。"),
            D(MetricKeys.HellstoneCount, "狱石", "Hellstone", "物块", true, "资源", "狱石物块数。"),
            D(MetricKeys.DesertFossilCount, "沙漠化石", "Desert fossil", "物块", true, "资源", "沙漠化石物块数。"),
            D(MetricKeys.MinecartTrackTiles, "矿车轨道", "Minecart track", "格", true, "结构", "天然矿车轨道长度近似。"),
            D(MetricKeys.PyramidCount, "金字塔", "Pyramids", "个", true, "结构", "大型沙岩砖连通结构数。"),
            D(MetricKeys.SwordShrineCount, "真剑冢", "Real sword shrines", "个", true, "结构", "检测真附魔剑物块 style 17；泰拉魔刃掉落仍在击碎时随机。"),
            D(MetricKeys.LivingTreeCount, "生命树", "Living trees", "个", true, "结构", "由生命木、树叶构成并合并近邻碎片后的可见结构数。"),
            D(MetricKeys.FloatingIslandCount, "浮空岛", "Floating islands", "个", true, "结构", "由云、雨云、雪云及天域物块识别的可见浮岛/天空湖数；会合并同一岛的装饰云碎片。"),
            D(MetricKeys.HiveCount, "蜂巢", "Hives", "个", true, "结构", "蜂巢物块/墙连通区域数。"),
            D(MetricKeys.MarbleCaveCount, "大理石洞", "Marble caves", "个", true, "结构", "大理石物块/墙连通区域数。"),
            D(MetricKeys.GraniteCaveCount, "花岗岩洞", "Granite caves", "个", true, "结构", "花岗岩物块/墙连通区域数。"),
            D(MetricKeys.SpiderCaveCount, "蜘蛛洞", "Spider caves", "个", true, "结构", "蜘蛛墙连通区域数。"),
            D(MetricKeys.MushroomBiomeCount, "发光蘑菇地", "Glowing mushroom biomes", "个", true, "结构", "蘑菇草连通区域数。"),
            D(MetricKeys.ShimmerLiquidTiles, "微光液体", "Shimmer liquid", "格", true, "资源", "含微光的物格数，不改变地图探索状态。"),
            D(MetricKeys.TrapCount, "陷阱风险", "Trap hazards", "个", false, "风险", "巨石、炸药与喷泉陷阱物块合计。"),
            D(MetricKeys.LavaCellCount, "岩浆区域", "Lava cells", "8×8区块", false, "风险", "含岩浆的降采样区块数。")
        ];

        foreach (ImportantItem item in ImportantItemCatalog.All)
        {
            result.Add(D(MetricKeys.ItemCount(item.Id), $"{item.ChineseName}数量",
                $"{item.EnglishName} count", "件", true, "宝箱物品", $"宝箱内 {item.ChineseName} 的总堆叠数。"));
            result.Add(D(MetricKeys.ItemNearestTiles(item.Id), $"最近{item.ChineseName}",
                $"Nearest {item.EnglishName}", "格", false, "宝箱物品", "到出生点的直线距离；不存在时为无穷。"));
            result.Add(D(MetricKeys.ItemAccessCost(item.Id), $"{item.ChineseName}获取成本",
                $"{item.EnglishName} access cost", "估算分", false, "宝箱物品", "8×8 降采样寻路，考虑挖掘、液体与阶段锁；仅用于相对比较。"));
        }
        return result.AsReadOnly();
    }

    private static MetricDefinition D(string key, string zh, string en, string unit, bool larger,
        string category, string description) => new(key, zh, en, unit,
            larger ? MetricPreference.LargerIsBetter : MetricPreference.SmallerIsBetter,
            category, description);
}

public static class ProfileEvaluator
{
    public static WorldEvaluation Evaluate(IReadOnlyDictionary<string, double> metrics, RollProfile profile)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(profile);
        bool hardPassed = true;
        double score = 0;
        List<CriterionResult> results = [];
        foreach (CriterionDefinition criterion in profile.Criteria.Where(c => c.Enabled))
        {
            bool exists = metrics.TryGetValue(criterion.MetricKey, out double actual);
            bool passed = exists && Compare(actual, criterion);
            if (criterion.Kind == CriterionKind.Hard && !passed) hardPassed = false;
            double contribution = criterion.Kind == CriterionKind.Weighted
                ? criterion.Weight * Score(actual, criterion, exists) * 100d
                : 0;
            score += contribution;
            string label = criterion.Label ??
                (MetricCatalog.ByKey.TryGetValue(criterion.MetricKey, out MetricDefinition? definition)
                    ? definition.ChineseName : criterion.MetricKey);
            results.Add(new CriterionResult(criterion, passed, actual, contribution,
                exists ? $"{label}: {Format(actual)} ({(passed ? "符合" : "不符合")})" : $"{label}: 指标不存在"));
        }
        return new WorldEvaluation(hardPassed, score, results.AsReadOnly());
    }

    private static bool Compare(double actual, CriterionDefinition c) => c.Comparison switch
    {
        MetricComparison.AtLeast => actual >= c.Value,
        MetricComparison.AtMost => actual <= c.Value,
        MetricComparison.Equal => Math.Abs(actual - c.Value) < 1e-9,
        MetricComparison.NotEqual => Math.Abs(actual - c.Value) >= 1e-9,
        MetricComparison.Between => actual >= Math.Min(c.Value, c.SecondValue) &&
                                    actual <= Math.Max(c.Value, c.SecondValue),
        _ => false
    };

    private static double Score(double actual, CriterionDefinition c, bool exists)
    {
        if (!exists || double.IsNaN(actual)) return -1;
        double scale = Math.Max(1, Math.Max(Math.Abs(c.Value), Math.Abs(c.SecondValue)));
        return c.Comparison switch
        {
            MetricComparison.AtLeast => Math.Clamp((actual - c.Value) / scale + 1, -1, 2),
            MetricComparison.AtMost => Math.Clamp((c.Value - actual) / scale + 1, -1, 2),
            MetricComparison.Equal => Math.Clamp(1 - Math.Abs(actual - c.Value) / scale, -1, 1),
            MetricComparison.NotEqual => Math.Abs(actual - c.Value) < 1e-9 ? -1 : 1,
            MetricComparison.Between => ScoreBetween(actual, c.Value, c.SecondValue),
            _ => 0
        };
    }

    private static double ScoreBetween(double actual, double first, double second)
    {
        double min = Math.Min(first, second), max = Math.Max(first, second);
        double width = Math.Max(1, max - min);
        if (actual < min) return Math.Clamp(1 - (min - actual) / width, -1, 1);
        if (actual > max) return Math.Clamp(1 - (actual - max) / width, -1, 1);
        double centre = (min + max) / 2;
        return 1 - Math.Abs(actual - centre) / (width + 1) * 0.1;
    }

    private static string Format(double value) => double.IsPositiveInfinity(value) ? "不存在" : value.ToString("0.##");
}

public static class BuiltInProfiles
{
    public static IReadOnlyList<RollProfile> All { get; } = new[]
    {
        SafeAndTidy(), Speedrun(), Collector(), Builder(), ResourceRich()
    };

    public static RollProfile Get(string name) => All.FirstOrDefault(p =>
        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ??
        throw new KeyNotFoundException($"Unknown profile '{name}'.");

    public static RollProfile SafeAndTidy() => new()
    {
        Name = "安全整洁",
        Description = "限制肉前邪恶的实际宽度与自由蔓延走廊，保护出生点和丛林。",
        Criteria =
        [
            C(MetricKeys.EvilLargestWidthTiles, CriterionKind.Hard, MetricComparison.AtMost, 1400, 0, 1, "邪恶实际宽度 ≤ 1400"),
            C(MetricKeys.EvilPreHardmodeClosureLargestWidth, CriterionKind.Hard, MetricComparison.AtMost, 1800, 0, 1, "肉前自由蔓延宽度 ≤ 1800"),
            C(MetricKeys.EvilNearestSpawnTiles, CriterionKind.Hard, MetricComparison.AtLeast, 350, 0, 1, "邪恶远离出生点"),
            C(MetricKeys.EvilJungleGapTiles, CriterionKind.Weighted, MetricComparison.AtLeast, 300, 0, 4, "邪恶远离丛林"),
            C(MetricKeys.EvilIsolationShaftEstimate, CriterionKind.Weighted, MetricComparison.AtMost, 7000, 0, 3, "较少隔离施工")
        ]
    };

    public static RollProfile Speedrun() => new()
    {
        Name = "速通",
        Description = "偏好早期机动、镜子、短行程地牢/丛林与易获取战利品。",
        Criteria =
        [
            C(MetricKeys.ItemCount(50), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 6, "至少一面魔镜"),
            C(MetricKeys.ItemCount(54), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 6, "赫尔墨斯靴"),
            C(MetricKeys.ItemCount(1579), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 4, "疾风雪靴"),
            C(MetricKeys.ItemAccessCost(50), CriterionKind.Weighted, MetricComparison.AtMost, 900, 0, 5, "魔镜易取得"),
            C(MetricKeys.DungeonDistanceTiles, CriterionKind.Weighted, MetricComparison.AtMost, 1800, 0, 2, "地牢较近"),
            C(MetricKeys.JungleDistanceTiles, CriterionKind.Weighted, MetricComparison.AtMost, 1400, 0, 2, "丛林较近"),
            C(MetricKeys.SpawnFlatness, CriterionKind.Weighted, MetricComparison.AtLeast, 35, 0, 1, "出生点平整")
        ]
    };

    public static RollProfile Collector() => new()
    {
        Name = "全收集",
        Description = "偏好稀有结构、功能站与不可再生宝箱物品。",
        Criteria =
        [
            C(MetricKeys.PyramidCount, CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 8, "金字塔"),
            C(MetricKeys.SwordShrineCount, CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 6, "真剑冢"),
            C(MetricKeys.LivingTreeCount, CriterionKind.Weighted, MetricComparison.AtLeast, 2, 0, 3, "生命树"),
            C(MetricKeys.FloatingIslandCount, CriterionKind.Weighted, MetricComparison.AtLeast, 3, 0, 3, "浮空岛"),
            C(MetricKeys.ItemCount(857), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 8, "沙暴瓶"),
            C(MetricKeys.ItemCount(2196), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 4, "生命木织机"),
            C(MetricKeys.ItemCount(2204), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 3, "蜂蜜分配机"),
            C(MetricKeys.ItemCount(3000), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 5, "炼药桌"),
            C(MetricKeys.ChestCount, CriterionKind.Weighted, MetricComparison.AtLeast, 250, 0, 2, "宝箱丰富")
        ]
    };

    public static RollProfile Builder() => new()
    {
        Name = "建筑养老",
        Description = "偏好平坦出生点、安全邪恶、齐全小生态与建设素材。",
        Criteria =
        [
            C(MetricKeys.SpawnFlatness, CriterionKind.Hard, MetricComparison.AtLeast, 25, 0, 1, "出生点不陡峭"),
            C(MetricKeys.EvilNearestSpawnTiles, CriterionKind.Hard, MetricComparison.AtLeast, 450, 0, 1, "邪恶远离出生点"),
            C(MetricKeys.EvilPreHardmodeClosureLargestWidth, CriterionKind.Weighted, MetricComparison.AtMost, 1800, 0, 5, "肉前蔓延易控制"),
            C(MetricKeys.MarbleCaveCount, CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 2, "大理石洞"),
            C(MetricKeys.GraniteCaveCount, CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 2, "花岗岩洞"),
            C(MetricKeys.MushroomBiomeCount, CriterionKind.Weighted, MetricComparison.AtLeast, 2, 0, 2, "蘑菇地"),
            C(MetricKeys.ItemCount(2196), CriterionKind.Weighted, MetricComparison.AtLeast, 1, 0, 3, "生命木织机")
        ]
    };

    public static RollProfile ResourceRich() => new()
    {
        Name = "资源富集",
        Description = "偏好矿物、宝石、生命水晶、轨道和宝箱总量。",
        Criteria =
        [
            C(MetricKeys.OreTileCount, CriterionKind.Weighted, MetricComparison.AtLeast, 18000, 0, 5, "肉前矿物"),
            C(MetricKeys.GemTileCount, CriterionKind.Weighted, MetricComparison.AtLeast, 1100, 0, 3, "宝石"),
            C(MetricKeys.HellstoneCount, CriterionKind.Weighted, MetricComparison.AtLeast, 8000, 0, 3, "狱石"),
            C(MetricKeys.LifeCrystalCount, CriterionKind.Weighted, MetricComparison.AtLeast, 250, 0, 4, "生命水晶"),
            C(MetricKeys.MinecartTrackTiles, CriterionKind.Weighted, MetricComparison.AtLeast, 3000, 0, 3, "矿车轨道"),
            C(MetricKeys.ChestCount, CriterionKind.Weighted, MetricComparison.AtLeast, 300, 0, 4, "宝箱")
        ]
    };

    private static CriterionDefinition C(string metric, CriterionKind kind, MetricComparison comparison,
        double value, double second, double weight, string label) => new()
        {
            MetricKey = metric,
            Kind = kind,
            Comparison = comparison,
            Value = value,
            SecondValue = second,
            Weight = weight,
            Label = label
        };
}
