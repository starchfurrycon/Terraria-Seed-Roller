namespace TerrariaSeedRoller.Core;

public sealed record ImportantItem(int Id, string EnglishName, string ChineseName, string Category, double DefaultWeight);

public static class ImportantItemCatalog
{
    private static readonly ImportantItem[] Items =
    [
        new(49, "Band of Regeneration", "再生手环", "Early mobility & survival", 2),
        new(50, "Magic Mirror", "魔镜", "Travel", 5),
        new(53, "Cloud in a Bottle", "云朵瓶", "Mobility", 4),
        new(54, "Hermes Boots", "赫尔墨斯靴", "Mobility", 5),
        new(65, "Starfury", "星怒", "Sky loot", 3),
        new(113, "Magic Missile", "魔法导弹", "Dungeon loot", 2),
        new(155, "Muramasa", "村正", "Dungeon loot", 4),
        new(156, "Cobalt Shield", "钴护盾", "Dungeon loot", 5),
        new(157, "Aqua Scepter", "海蓝权杖", "Dungeon loot", 2),
        new(158, "Lucky Horseshoe", "幸运马掌", "Sky loot", 4),
        new(159, "Shiny Red Balloon", "闪亮红气球", "Sky loot", 5),
        new(163, "Blue Moon", "蓝月", "Dungeon loot", 2),
        new(164, "Handgun", "手枪", "Dungeon loot", 4),
        new(186, "Breathing Reed", "呼吸管", "Water loot", 1),
        new(211, "Feral Claws", "猛爪手套", "Jungle loot", 4),
        new(212, "Anklet of the Wind", "疾风脚镯", "Jungle loot", 5),
        new(213, "Staff of Regrowth", "再生法杖", "Jungle loot", 3),
        new(277, "Trident", "三叉戟", "Water loot", 2),
        new(285, "Aglet", "金属带扣", "Surface loot", 4),
        new(329, "Shadow Key", "暗影钥匙", "Dungeon loot", 5),
        new(670, "Ice Boomerang", "冰雪回旋镖", "Ice loot", 2),
        new(724, "Ice Blade", "冰雪刃", "Ice loot", 2),
        new(832, "Living Wood Wand", "生命木魔棒", "Living tree loot", 2),
        new(848, "Pharaoh's Mask", "法老面具", "Pyramid loot", 1),
        new(857, "Sandstorm in a Bottle", "沙暴瓶", "Pyramid loot", 8),
        new(863, "Water Walking Boots", "水上漂靴", "Water loot", 6),
        new(866, "Pharaoh's Robe", "法老长袍", "Pyramid loot", 1),
        new(906, "Lava Charm", "熔岩护身符", "Cavern loot", 7),
        new(930, "Flare Gun", "信号枪", "Underground loot", 1),
        new(933, "Leaf Wand", "树叶魔棒", "Living tree loot", 2),
        new(950, "Ice Skates", "溜冰鞋", "Ice loot", 6),
        new(953, "Climbing Claws", "攀爬爪", "Mobility", 4),
        new(964, "Boomstick", "三发猎枪", "Jungle loot", 4),
        new(975, "Shoe Spikes", "鞋钉", "Mobility", 4),
        new(987, "Blizzard in a Bottle", "暴雪瓶", "Ice loot", 4),
        new(989, "Enchanted Sword", "附魔剑", "Sword shrine", 7),
        new(997, "Extractinator", "提炼机", "Utility", 5),
        new(1319, "Snowball Cannon", "雪球炮", "Ice loot", 1),
        new(1579, "Flurry Boots", "疾风雪靴", "Mobility", 5),
        new(2192, "Bone Welder", "骨头焊机", "Dungeon station", 2),
        new(2196, "Living Loom", "生命木织机", "Living tree loot", 3),
        new(2197, "Sky Mill", "天磨", "Sky loot", 3),
        new(2204, "Honey Dispenser", "蜂蜜分配机", "Hive station", 2),
        new(2292, "Fiberglass Fishing Pole", "玻璃钢钓竿", "Jungle loot", 3),
        new(2999, "Bewitching Table", "施法桌", "Dungeon station", 4),
        new(3000, "Alchemy Table", "炼药桌", "Dungeon station", 5),
        new(3017, "Flower Boots", "花靴", "Jungle loot", 4),
        new(3068, "Guide to Plant Fiber Cordage", "植物纤维绳索宝典", "Surface loot", 2),
        new(3084, "Radar", "雷达", "Surface loot", 3),
        new(3199, "Ice Mirror", "冰雪镜", "Travel", 5),
        new(3200, "Sailfish Boots", "旗鱼靴", "Mobility", 5),
        new(3225, "Balloon Pufferfish", "气球河豚鱼", "Fishing mobility", 2),
        new(3317, "Valor", "英勇球", "Dungeon loot", 2),
        new(3360, "Living Mahogany Wand", "生命红木魔棒", "Jungle loot", 2),
        new(4055, "Dunerider Boots", "沙丘行者靴", "Mobility", 5),
        new(4061, "Storm Spear", "风暴长矛", "Desert loot", 2),
        new(4062, "Thunder Zapper", "雷霆法杖", "Desert loot", 2),
        new(4144, "Terragrim", "泰拉魔刃", "Sword shrine", 10),
        new(4263, "Magic Conch", "魔法海螺", "Travel", 6),
        new(4276, "Bast Statue", "巴斯特雕像", "Desert loot", 5),
        new(4281, "Finch Staff", "雀杖", "Living tree loot", 4),
        new(4341, "Step Stool", "梯凳", "Surface loot", 1),
        new(4346, "Encumbering Stone", "负重石", "Desert loot", 1),
        new(4425, "Shark Bait", "鲨鱼鱼饵", "Ocean loot", 1),
        new(4819, "Demon Conch", "恶魔海螺", "Travel", 6),
        new(5010, "Treasure Magnet", "宝藏磁石", "Underworld loot", 3),
        new(5011, "Mace", "链锤", "Underground loot", 1)
    ];

    private static readonly Dictionary<int, ImportantItem> ByIdMap = Items.ToDictionary(item => item.Id);

    public static IReadOnlyList<ImportantItem> All { get; } = Array.AsReadOnly(Items);
    public static IReadOnlyDictionary<int, ImportantItem> ById { get; } = ByIdMap;

    public static string DisplayName(int itemId, bool chinese = true) =>
        ByIdMap.TryGetValue(itemId, out ImportantItem? item)
            ? chinese ? item.ChineseName : item.EnglishName
            : $"Item #{itemId}";
}
