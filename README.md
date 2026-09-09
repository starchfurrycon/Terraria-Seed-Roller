# Terraria Seed Roller / 泰拉瑞亚 Roll 种机

面向原版 Terraria 的独立、只读世界筛选器。它调用你已经安装的 `TerrariaServer.exe` 在隐藏后台真实生成候选世界，再分析最终 `.wld`，按硬条件淘汰、按加权条件排名，只保留最好的若干世界。

这不是 tModLoader Mod，不修改游戏安装，也不靠不完整的随机数逆推。宝箱内容、微光、结构和地形都来自对应版本游戏真正生成出的世界。

## 核心能力

- 中文 WinForms 图形界面，另有适合无人值守和自动化的 CLI/JSON。
- 世界大小、难度、腐化/猩红、全部 1.4.5 特殊种子组合、整数种子范围和无重复随机顺序。
- 硬条件与加权条件分离：先排除不合格世界，再对合格世界排名。
- 预设：安全整洁、速通、全收集、建筑养老、资源富集；所有条件都可编辑。
- 肉前邪恶：当前区域宽度、数量、离出生点/丛林/地牢距离、隔离施工估算，以及沿暴露土壤直接相邻传播、含有限荆棘跨隙预算与向日葵正下方保护的自由蔓延闭包。
- 宝箱与特殊资源：数量、直线距离、箱型、阶段锁，以及沿最低成本路线累计距离、垂直施工、挖掘、无支撑高空、危险群系/结构和液体后的 8×8 降采样获取成本。
- 结构：金字塔、真剑冢、生命树、浮空岛、蜂巢、微光、地牢、神庙、蜘蛛洞、蘑菇地、大理石洞、花岗岩洞。
- 资源与风险：宝箱、生命水晶、祭坛、暗影珠/猩红之心、四级肉前矿、宝石、狱石、沙漠化石、提炼机、轨道、岩浆和天然陷阱。
- 世界变体：邪恶类型、地牢/丛林方向、铜/锡、铁/铅、银/钨、金/铂。
- 任意 Tile、Wall、Item ID 的通用指标，能够在游戏更新或特殊需求下直接扩展条件。
- 每个入选世界附带 JSON、HTML 和 SVG 报告；专用地图不读取或写入玩家 `.map`，不会解锁游戏视野。
- 暂停、取消、单世界超时、并发限制、磁盘余量保护和失败日志。

## 兼容性

- 已端到端验证：Windows 原版 Terraria `1.4.5.8`，世界格式 `326`。
- 读取器支持世界格式 `269–326`（约 Terraria 1.4.3 至 1.4.5.8）。未知的新格式会明确拒绝，不会猜格式或写入文件。
- 生成结果始终由用户本机的 TerrariaServer 版本决定。不同 Terraria 版本即便复制种子相同，也不保证最终世界相同。
- 当前发布目标是 Windows x64，因为 Steam 原版服务端和图形界面均在 Windows 上验证。

## 性能与资源占用

- 精确模式必须让原版完成一次世界生成；完整宝箱和最终地形在此之前并不存在。数学逆推型工具可以每秒筛大量种子，但只能覆盖少量早期随机数特征，不能替代完整世界验证。
- 1.4.5.8 小世界实测基准中，同一种子由原版生成约需 17 秒，完整只读分析约需 0.4 秒；机器、磁盘、世界大小和特殊种子会改变结果。GUI 与 CLI 会分别显示生成和分析耗时。
- 生成器在收到 `Server started` 后验证 `.wld` 分区表和完整 Footer，立即安全退出，不再为每个世界固定多等约 1.4 秒；分析器合并整图扫描，并在所有重要目标路线都已确定后提前结束寻路。
- 默认并发数 1、资源策略 `Balanced`，避免多个约需 500 MB 或更多内存的原版服务端互相争抢。`Fastest` 会提高进程优先级但不会减少总计算量；`LowImpact`/`Idle` 更照顾前台游戏，代价是 Roll 种更慢。

## 快速开始

1. 从 GitHub Releases 下载 `TerrariaSeedRoller-win-x64.zip` 并解压。
2. 运行 `TerrariaSeedRoller.exe`。
3. 确认 `TerrariaServer.exe` 路径。Steam 常见位置会自动检测。
4. 选择输出目录、世界参数、尝试数量和预设。
5. 检查“筛选条件”页，再点“开始 Roll 种”。

默认并发数为 1，适合边做别的事边后台生成。提高并发会显著增加 CPU、内存和磁盘压力，也可能令游戏本身卡顿；若同时游玩，优先使用“低影响”资源策略而不是盲目增加并发。

GUI 的“只读分析现有世界”可以分析任意受支持 `.wld`。它以共享只读方式打开文件，并将报告写到独立输出目录。

## CLI

```powershell
TerrariaSeedRoller.Cli.exe init roller.json
TerrariaSeedRoller.Cli.exe roll --config roller.json
TerrariaSeedRoller.Cli.exe analyze "D:\Worlds\MyWorld.wld" --profile 安全整洁 --output report
TerrariaSeedRoller.Cli.exe presets
TerrariaSeedRoller.Cli.exe metrics
```

按 `Ctrl+C` 会取消任务。程序只会终止它自己刚刚启动且持有 PID 的服务端子进程，不会枚举或关闭用户正在玩的 Terraria。

配置文件由 `init` 生成。主要字段：

```json
{
  "Generation": {
    "TerrariaServerPath": "D:\\Steam\\steamapps\\common\\Terraria\\TerrariaServer.exe",
    "CandidateDirectory": "D:\\Terraria-Rolls",
    "Size": "Small",
    "Difficulty": "Classic",
    "Evil": "Random",
    "SpecialSeeds": "None",
    "Seeds": { "Start": 0, "End": 2147483647, "RandomOrder": true },
    "MaximumAttempts": 20,
    "WinnersToKeep": 3,
    "Parallelism": 1,
    "ServerPriority": "Balanced",
    "PerWorldTimeout": "00:10:00",
    "KeepRejectedWorlds": false,
    "TopResultsToTrack": 50,
    "MinimumFreeDiskGb": 2
  },
  "Profile": {
    "Name": "自定义",
    "Description": "",
    "Criteria": []
  }
}
```

条件比较支持 `AtLeast`、`AtMost`、`Equal`、`NotEqual`、`Between`；类型支持 `Hard` 和 `Weighted`。在 GUI 中可以从指标目录添加，也可在 JSON 中直接写：

- `tiles.<TileID>.count`
- `walls.<WallID>.count`
- `loot.item.<ItemID>.count`
- `loot.item.<ItemID>.chests`
- `loot.item.<ItemID>.nearestTiles`
- `loot.item.<ItemID>.accessCost`

完整内置键见 [`docs/metrics.md`](docs/metrics.md) 或运行 `metrics` 命令。

## 结果目录

每次运行创建独立 `roll_时间_预设` 目录：

```text
roll_20260909_200045_安全整洁/
├─ session.json
├─ failures.log                 # 仅在有失败时出现
└─ winners/
   ├─ 01_seed_..._score_....wld
   └─ 01_seed_..._score_.../
      ├─ analysis.json
      ├─ report.html
      └─ overview.svg
```

默认情况下，未通过硬条件以及跌出前 N 名的临时世界会立即删除；勾选“保留未通过世界”后才会保留到 `rejected`。程序删除前会验证目标一定在当前输出目录的 `_work` 子目录内。

## 指标解释与限制

“Roll 种”没有一个适用于所有玩家的唯一最优解，因此项目同时提供硬条件、权重、预设和通用 ID 指标。算法细节见 [`docs/algorithm.md`](docs/algorithm.md)。特别注意：

- 肉前传播闭包是面向比较和筛选的保守模型，不是把世界运行无限游戏刻后的逐帧仿真。它只建模肉前会自然前进的地表通道；困难模式三格感染另属后续隔离问题。
- 获取成本是一次 8×8 降采样 Dijkstra 的相对分数。算法实际寻找总成本最低的路线，而不是把直线距离乘常数：普通地形、丛林、邪恶、雪原、地表/地下沙漠、蜘蛛洞、蜂巢、地牢、神庙、水、岩浆、垂直移动和高空架桥的代价不同，锁箱与神庙另加阶段惩罚。它不假装模拟某套具体装备、炸药或玩家操作。
- 真剑冢的 tile/style 在世界中可以确定。Terraria 1.4.5.8 的泰拉魔刃是击碎真剑冢时才以 1/30 掷出，因而任何“保证泰拉魔刃种子”的说法都不可靠。
- 特殊种子会大幅改变世界规则。通用指标仍然可用，但普通世界预设的阈值未必合适，应自行调整。

## 安全设计

- `.wld` 仅以 `FileAccess.Read` 和共享读取方式打开；分析器没有世界写入代码。
- 不读取 `.plr` 或 `.map`，不改变云存档、Steam Workshop 或游戏安装。
- 候选世界写入用户指定的项目输出目录，不进入真实 Worlds 目录。
- 服务端使用隐藏子进程、重定向输入输出、`port=0` 和 `upnp=0`。
- Windows 原版服务端强制控制台输入为 UTF-16LE；生成完成后程序发送原始 UTF-16LE `exit-nosave`，这是端到端测试覆盖的退出路径。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build TerrariaSeedRoller.sln -c Release
dotnet run --project tests/TerrariaSeedRoller.Tests -c Release
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

集成测试可把一个世界路径作为参数传给测试程序，或设置 `TSR_TEST_WORLD`。测试会在分析前后核对长度、最后写入时间和 SHA-256。

## 机制资料

设计和核对使用官方 Terraria Wiki：

- [World Seed](https://terraria.wiki.gg/wiki/World_Seed)
- [Server configuration and priority](https://terraria.wiki.gg/wiki/Server)
- [Secret world seeds](https://terraria.wiki.gg/wiki/Secret_world_seeds)
- [Biome spread](https://terraria.wiki.gg/wiki/Biome_spread)
- [The Corruption](https://terraria.wiki.gg/wiki/The_Corruption) / [The Crimson](https://terraria.wiki.gg/wiki/The_Crimson)
- [The Aether](https://terraria.wiki.gg/wiki/The_Aether)
- [Pyramid](https://terraria.wiki.gg/wiki/Pyramid)
- [Enchanted Sword Shrine](https://terraria.wiki.gg/wiki/Enchanted_Sword_Shrine)
- [Chest loot](https://terraria.wiki.gg/wiki/Chest_loot)

开发时也只为兼容性核对参考了本机 Terraria 反编译结构；仓库及二进制不包含 Terraria 源码、资源或贴图。

## License

项目源码采用 [MIT License](LICENSE)。Terraria 是 Re-Logic 的商标和版权作品；本项目与 Re-Logic 无隶属关系，运行时需要用户自行合法安装 Terraria。

---

English summary: a Windows desktop/CLI seed searcher that asks the installed vanilla TerrariaServer to generate real worlds, then performs read-only `.wld` analysis, hard filtering, weighted ranking and self-contained reports. It never modifies player worlds or map exploration data.
