# 指标目录

运行 `TerrariaSeedRoller.Cli.exe metrics` 可以查看当前版本的完整内置目录。GUI 的“添加指标”使用同一目录。

主要键组：

| 前缀 | 内容 |
|---|---|
| `world.*` | 邪恶、地牢/丛林方向、四级矿物变体 |
| `evil.regions.*` | 当前邪恶区域数量与宽度 |
| `evil.prehardmode.*` | 肉前地表自然传播闭包 |
| `evil.*GapTiles` | 邪恶与丛林/地牢间距 |
| `spawn.*` | 平整度、最近宝箱/生命水晶 |
| `travel.*` | 地牢、丛林、雪原、沙漠、微光行程 |
| `structures.*` | 金字塔、真剑冢、生命树、浮岛、蜂巢等 |
| `resources.*` | 水晶、祭坛、矿、宝石、狱石、微光等 |
| `hazards.*` | 岩浆和陷阱 |
| `loot.item.<ID>.*` | 宝箱物品数量、箱数、最近距离和获取成本 |

通用、无需代码改动的键：

- `tiles.<TileID>.count`
- `walls.<WallID>.count`
- `loot.item.<ItemID>.count`
- `loot.item.<ItemID>.chests`
- `loot.item.<ItemID>.nearestTiles`
- `loot.item.<ItemID>.accessCost`

内置重要物品覆盖镜子、各类靴子、瓶子、熔岩护身符、冰鞋、沙暴瓶、水上漂靴、金属带扣、疾风脚镯、猛爪手套、花靴、三发猎枪、附魔剑、地牢主要物品、暗影钥匙、功能站、海螺、恶魔海螺、巴斯特雕像和雀杖等。

距离单位为物块格。`accessCost` 是相对估算分而非格数；只能与相同世界大小/算法版本的结果比较。
