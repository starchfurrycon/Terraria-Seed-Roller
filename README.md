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
- 资源保护：内存保留线、预留逻辑处理器、服务端内存上限与静默看门狗。机器被别的任务抢占时降速或等待，而不是把内存耗光后卡死。
- 意外中断保护：每个世界分析完立即复制出临时目录并写入日志，中断的会话可以继续，也可以直接导出已经完成的世界。

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

## 资源保护

一次 Roll 种要连续跑很久，中途被别的任务挤掉内存就前功尽弃，所以工具为自己划了一条最低资源线。默认值开箱可用，全部可在 JSON 里调整：

- `MinimumFreeMemoryMb`（默认 `1536`）：系统可用物理内存低于这条线时不再启动新世界。低于 `保留线 − 256 MB` 时，工具会挂起它自己启动的服务端，把 CPU 和内存让给“分析当前世界”和“保存已完成结果”这两件必须做完的事。可用内存恢复后自动继续。上限被限制为物理内存的四分之一，避免在小内存机器上把 Roll 种锁死。
- `ReservedLogicalProcessors`（默认 `1`）：从并发预算里扣掉的逻辑处理器数量。4 核机器即使填并发 4，实际最多同时跑 3 个服务端，留下的那个核心负责界面、分析和写盘。
- `ServerMemoryLimitMb`（默认 `3072`）：通过 Windows 作业对象给每个服务端设的硬上限，防止单个进程失控把整机拖进页面文件。
- `ServerStallTimeout`（默认 `3` 分钟）：服务端连续这么久没有任何输出就判定卡住，杀掉并按失败处理，而不是占着并发位等到单世界超时。正常生成会持续上报进度，不会误杀慢而健康的世界。
- `ProtectProcessPriority`（默认 `true`）：把本进程优先级提到 `AboveNormal`，使界面和保存路径不会被它自己启动的服务端饿死。

工具还会在每次运行时清理上一次中断留下的服务端进程和临时生成目录：所有服务端都被加入一个 `KILL_ON_JOB_CLOSE` 作业对象，即使主进程崩溃或被强制结束，Windows 也会连带终止它们，不会留下继续吃内存的孤儿进程。

## 意外中断保护

“中断就等于白跑”已经被去掉，代价是每个世界多一次本地复制（小世界约 10 MB）：

- 每个世界通过硬条件后立刻从 `_work` 临时目录复制到会话的 `winners/staging/`，然后才写日志。崩溃最多损失正在生成的那一个世界。
- 每次尝试的结果都会追加写入 `session.journal.jsonl` 并强制刷盘；开始生成前也会先记一条“进行中”，所以中断后能知道哪些临时目录和服务端需要清理。
- `session.state.json` 记录会话是否正常结束。仍然写着 `Running` 的会话就是被中断的。
- 结束时会话状态、排名和报告照常写入；恢复会话时只重算最终入围世界的完整报告，排名沿用日志里的分数。

## 快速开始

1. 从 GitHub Releases 下载 `TerrariaSeedRoller-win-x64.zip` 并解压。
2. 运行 `TerrariaSeedRoller.exe`。
3. 确认 `TerrariaServer.exe` 路径。Steam 常见位置会自动检测。
4. 选择输出目录、世界参数、尝试数量和预设。
5. 检查“筛选条件”页，再点“开始 Roll 种”。

默认并发数为 1，适合边做别的事边后台生成。提高并发会显著增加 CPU、内存和磁盘压力，也可能令游戏本身卡顿；若同时游玩，优先使用“低影响”资源策略而不是盲目增加并发。

GUI 的“只读分析现有世界”可以分析任意受支持 `.wld`。它以共享只读方式打开文件，并将报告写到独立输出目录。

## 图形界面

界面完全自绘，不使用系统 `TabControl`、`DataGridView`、`ComboBox` 或滚动条，因此不会出现浅色系统主题控件、错位的表格子行或“选中才看得见”的文字。整体分为左侧设置栏、右侧工作区和底部操作栏。

工作区三页：

- **筛选条件** — 指标、比较方式、阈值、权重与启用开关的列表，可直接双击编辑、切换启用或删除；工具栏提供“添加指标”“载入预设”“全部启用/停用”。
- **候选结果** — 上表按加权得分排名（种子、得分、硬条件、生成耗时、世界路径），下表左侧是世界俯视地图（可缩放、拖动、一键适配，含图例），右侧是分组明细（世界变体、结构、资源、风险、路线成本）。
- **运行日志** — 按级别着色的实时日志，支持复制与打开输出目录。

界面支持 150%/200% 显示缩放与多显示器 DPI 变化，窗口会按实际工作区自动收缩，不会超出屏幕。所有自绘控件都支持 Tab 焦点、回车/空格激活与方向键导航。

## 开发者诊断

发布版本内置若干无界面自检开关，用于在没有人盯着屏幕的情况下验证界面：

```powershell
# 布局与调色板对比度审计，输出文本报告；有裁剪、重叠或对比度不足时返回 2
TerrariaSeedRoller.exe --ui-audit="ui-audit.txt"

# 逐个工作区页截图，用于渲染后的对比度审计
TerrariaSeedRoller.exe --ui-shot="shots"

# 从同一套矢量图形重新生成多分辨率应用图标
TerrariaSeedRoller.exe --make-icon="app.ico"

# 不打开窗口，仅验证核心程序集可加载
TerrariaSeedRoller.exe --smoke-test
```

`scripts/shot-audit.ps1` 会读取截图，把每个文字像素追溯到它真正的背景色，并报告低于 4.5:1 的组合；当前发布界面的最差组合为 4.67:1。

## CLI

```powershell
TerrariaSeedRoller.Cli.exe init roller.json
TerrariaSeedRoller.Cli.exe roll --config roller.json [--resume | --fresh]
TerrariaSeedRoller.Cli.exe analyze "D:\Worlds\MyWorld.wld" --profile 安全整洁 --output report
TerrariaSeedRoller.Cli.exe sessions --output "D:\Terraria-Rolls"
TerrariaSeedRoller.Cli.exe recover --config roller.json
TerrariaSeedRoller.Cli.exe presets
TerrariaSeedRoller.Cli.exe metrics
```

按 `Ctrl+C` 会取消任务，取消同样会保留已完成的结果并允许稍后继续。程序只会终止它自己刚刚启动且持有 PID 的服务端子进程，不会枚举或关闭用户正在玩的 Terraria。

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
    "MinimumFreeDiskGb": 2,
    "MinimumFreeMemoryMb": 1536,
    "ReservedLogicalProcessors": 1,
    "ServerMemoryLimitMb": 3072,
    "ServerStallTimeout": "00:03:00",
    "ProtectProcessPriority": true
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
├─ session.json                  # 最终排名与会话摘要
├─ session.state.json            # 会话状态；仍是 Running 表示被中断
├─ session.journal.jsonl         # 逐次尝试日志，恢复会话时读取
├─ crash.log                     # 仅在进程异常终止时出现
├─ failures.log                  # 仅在有失败时出现
└─ winners/
   ├─ 01_seed_..._score_....wld
   └─ 01_seed_..._score_.../
      ├─ analysis.json
      ├─ report.html
      └─ overview.svg
```

被中断的会话不会静默消失：

```powershell
# 列出输出目录里所有可继续的会话
TerrariaSeedRoller.Cli.exe sessions --output "D:\Terraria-Rolls"

# 继续最近一个被中断的会话（跳过已经完成的世界，只补跑剩下的）
TerrariaSeedRoller.Cli.exe roll --config roller.json --resume

# 不生成任何新世界，直接导出中断会话里已经完成的世界和报告
TerrariaSeedRoller.Cli.exe recover --config roller.json
```

不加 `--resume` 时 `roll` 会开始一个新会话，并在日志里提示还有哪个旧会话可以继续。`--fresh` 用于在存在中断会话时明确要求开始新会话。继续会话前会核对世界参数，参数不一致会直接拒绝，避免把不同规则的世界混在一起排名。

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
- 每个服务端都被加入一个 `KILL_ON_JOB_CLOSE` Windows 作业对象，主进程结束（包括崩溃或被强制结束）时由系统连带终止，不会留下孤儿服务端。
- 工具只终止自己启动的服务端；中断遗留的进程通过输出目录下的 `server-pids.txt` 识别，正常结束时该文件会被清空。
- Windows 原版服务端强制控制台输入为 UTF-16LE；生成完成后程序发送原始 UTF-16LE `exit-nosave`，这是端到端测试覆盖的退出路径。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build TerrariaSeedRoller.sln -c Release
dotnet run --project tests/TerrariaSeedRoller.Tests -c Release
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

图标来自 `scripts/icons/` 中的 Lucide SVG 源，由 `scripts/generate-icons.ps1` 生成 `Design/IconData.cs`；应用图标由 `--make-icon` 从同一份图形数据生成。署名见 [`NOTICE.md`](NOTICE.md)。

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
