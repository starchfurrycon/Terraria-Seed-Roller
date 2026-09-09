namespace TerrariaSeedRoller.Core;

internal static class TileIds
{
    public const ushort Dirt = 0;
    public const ushort Stone = 1;
    public const ushort Grass = 2;
    public const ushort Iron = 6;
    public const ushort Copper = 7;
    public const ushort Gold = 8;
    public const ushort Silver = 9;
    public const ushort LifeCrystal = 12;
    public const ushort Containers = 21;
    public const ushort Demonite = 22;
    public const ushort CorruptGrass = 23;
    public const ushort CorruptPlants = 24;
    public const ushort Ebonstone = 25;
    public const ushort DemonAltar = 26;
    public const ushort ShadowOrbs = 31;
    public const ushort CorruptThorns = 32;
    public const ushort Meteorite = 37;
    public const ushort BlueDungeonBrick = 41;
    public const ushort GreenDungeonBrick = 43;
    public const ushort PinkDungeonBrick = 44;
    public const ushort Cobweb = 51;
    public const ushort Sand = 53;
    public const ushort Hellstone = 58;
    public const ushort Mud = 59;
    public const ushort JungleGrass = 60;
    public const ushort Vines = 52;
    public const ushort JungleVines = 62;
    public const ushort Sapphire = 63;
    public const ushort Ruby = 64;
    public const ushort Emerald = 65;
    public const ushort Topaz = 66;
    public const ushort Amethyst = 67;
    public const ushort Diamond = 68;
    public const ushort JungleThorns = 69;
    public const ushort MushroomGrass = 70;
    public const ushort HallowedGrass = 109;
    public const ushort Ebonsand = 112;
    public const ushort HallowedVines = 115;
    public const ushort Pearlsand = 116;
    public const ushort Pearlstone = 117;
    public const ushort Silt = 123;
    public const ushort Boulder = 138;
    public const ushort Explosives = 141;
    public const ushort SnowBlock = 147;
    public const ushort SandstoneBrick = 151;
    public const ushort IceBlock = 161;
    public const ushort CorruptIce = 163;
    public const ushort Tin = 166;
    public const ushort Lead = 167;
    public const ushort Tungsten = 168;
    public const ushort Platinum = 169;
    public const ushort Cloud = 189;
    public const ushort LivingWood = 191;
    public const ushort LeafBlock = 192;
    public const ushort RainCloud = 196;
    public const ushort CrimsonGrass = 199;
    public const ushort Sunplate = 202;
    public const ushort Crimstone = 203;
    public const ushort Crimtane = 204;
    public const ushort CrimsonVines = 205;
    public const ushort Slush = 224;
    public const ushort Hive = 225;
    public const ushort LihzahrdBrick = 226;
    public const ushort Larva = 231;
    public const ushort Crimsand = 234;
    public const ushort MinecartTrack = 314;
    public const ushort CrimsonThorns = 352;
    public const ushort MarbleBlock = 357;
    public const ushort Marble = 367;
    public const ushort Granite = 368;
    public const ushort GraniteBlock = 369;
    public const ushort Sandstone = 396;
    public const ushort CorruptHardenedSand = 398;
    public const ushort CrimsonHardenedSand = 399;
    public const ushort CorruptSandstone = 400;
    public const ushort CrimsonSandstone = 401;
    public const ushort DesertFossil = 404;
    public const ushort GeyserTrap = 443;
    public const ushort SnowCloud = 460;
    public const ushort Containers2 = 467;
    public const ushort MushroomVines = 528;
    public const ushort CorruptVines = 636;
    public const ushort CorruptJungleGrass = 661;
    public const ushort CrimsonJungleGrass = 662;
    public const ushort Sunflower = 27;
    public const ushort HallowedIce = 164;
    public const ushort CrimsonIce = 200;
    public const ushort GolfGrass = 477;
    public const ushort HallowedGolfGrass = 492;
    public const ushort HallowedHardenedSand = 402;
    public const ushort HallowedSandstone = 403;

    public static readonly HashSet<ushort> Evil =
    [
        CorruptGrass, CorruptPlants, Ebonstone, CorruptThorns, Ebonsand, CorruptIce,
        CrimsonGrass, Crimstone, CrimsonVines, Crimsand, CrimsonThorns,
        CorruptHardenedSand, CrimsonHardenedSand, CorruptSandstone, CrimsonSandstone,
        CorruptVines, CorruptJungleGrass, CrimsonJungleGrass, CrimsonIce, 201
    ];

    public static readonly HashSet<ushort> Corruption =
    [CorruptGrass, CorruptPlants, Ebonstone, CorruptThorns, Ebonsand, CorruptIce,
      CorruptHardenedSand, CorruptSandstone, CorruptVines, CorruptJungleGrass];

    public static readonly HashSet<ushort> Crimson =
    [CrimsonGrass, CrimsonVines, Crimstone, Crimsand, CrimsonThorns,
      CrimsonHardenedSand, CrimsonSandstone, CrimsonJungleGrass, CrimsonIce, 201];

    public static readonly HashSet<ushort> Hallow =
    [HallowedGrass, HallowedGolfGrass, Pearlstone, Pearlsand, HallowedIce,
      HallowedHardenedSand, HallowedSandstone, HallowedVines, 110, 113];

    // Tiles converted by natural hardmode spread. This deliberately follows the
    // conversion groups used by vanilla rather than treating every solid tile as
    // infectable.
    public static readonly HashSet<ushort> InfectionConvertible =
    [Dirt, Grass, GolfGrass, JungleGrass, JungleThorns, Stone, Sand, Sandstone,
      397, IceBlock, 182, 180, 179, 381, 183, 181, 534, 536, 539, 625, 627,
      .. Evil, .. Hallow];

    // Before Hardmode, evil grass/plants can advance along exposed dirt/grass and
    // new jungle grass variants. Sand, stone and ice need hardmode conversion.
    public static readonly HashSet<ushort> PreHardmodeSurfaceSpreadable =
    [Dirt, Grass, GolfGrass, HallowedGrass, HallowedGolfGrass,
      Mud, JungleGrass, CorruptJungleGrass, CrimsonJungleGrass,
      CorruptGrass, CrimsonGrass];

    public static readonly HashSet<ushort> Jungle =
    [Mud, JungleGrass, JungleVines, JungleThorns, 61, 74, 226, 231, 225];

    public static readonly HashSet<ushort> Snow =
    [SnowBlock, IceBlock, CorruptIce, 164, Slush, SnowCloud];

    public static readonly HashSet<ushort> Desert =
    [Sand, Ebonsand, Pearlsand, Crimsand, Sandstone, CorruptHardenedSand,
      CrimsonHardenedSand, CorruptSandstone, CrimsonSandstone, DesertFossil, 397, 402, 403];

    public static bool IsContainer(ushort type) => type is Containers or Containers2;
    public static bool IsDungeonBrick(ushort type) => type is BlueDungeonBrick or GreenDungeonBrick or PinkDungeonBrick;
    public static bool IsCloud(ushort type) => type is Cloud or RainCloud or SnowCloud or Sunplate;
    public static bool IsLivingTree(ushort type) => type is LivingWood or LeafBlock;
    public static bool IsMarble(ushort type) => type is Marble or MarbleBlock or 561;
    public static bool IsGranite(ushort type) => type is Granite or GraniteBlock or 576;
    public static bool IsPassableDecoration(ushort type) => type is
        3 or 4 or 5 or 13 or 20 or 24 or 27 or 33 or 49 or 52 or 61 or 62 or 69 or
        71 or 72 or 73 or 74 or 81 or 82 or 83 or 84 or 110 or 113 or 115 or 165 or
        174 or 184 or 185 or 186 or 187 or 201 or 205 or 227 or 233 or 270 or 271 or
        382 or 485 or 528 or 529 or 590 or 595 or 615 or 624 or 636 or 637 or 638 or
        655 or 656 or 700 or 701 or 703;
}

internal static class WallClasses
{
    public const byte None = 0;
    public const byte Dungeon = 1;
    public const byte Spider = 2;
    public const byte Hive = 3;
    public const byte Temple = 4;
    public const byte Marble = 5;
    public const byte Granite = 6;

    public static byte Classify(ushort wall)
    {
        // Unsafe dungeon walls occupy the historic 7-9 range and their slab/tile variants.
        if (wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99)
            return Dungeon;
        if (wall is 62 or 63)
            return Spider;
        if (wall is 86)
            return Hive;
        if (wall is 87)
            return Temple;
        if (wall is 178 or 179)
            return Marble;
        if (wall is 180 or 181)
            return Granite;
        return None;
    }
}
