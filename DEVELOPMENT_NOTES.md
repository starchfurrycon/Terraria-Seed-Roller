# Development notes

- Current verified vanilla target: Terraria 1.4.5.8, world format 326; reader range 269–326.
- World parsing must remain shared read-only and must reject unknown formats.
- Windows TerrariaServer forces UTF-16LE console input. Send raw `Encoding.Unicode` bytes for `exit-nosave`; ordinary redirected `WriteLine` may not exit.
- Launch with no shell/no window, redirected streams, `port=0`, `upnp=0`; manage only the exact child process object/PID created by this tool.
- Candidate attempts belong under `<CandidateDirectory>\_work`. Recursive cleanup must verify the canonical target begins with that exact root plus a directory separator.
- True enchanted-sword shrine: tile 187 uses Style3x2, style 17, top-left `frameX=918`, `frameY=0`. In 1.4.5 the Terragrim 1/30 roll occurs on tile break, not world generation.
- Chest style is `frameX / 36`. For tile 21, style 10 is Ivy Chest, style 12 Living Wood Chest, style 13 Skyware Chest, style 15 Web Covered Chest, style 17 Water Chest. Keep the full names aligned with vanilla `WorldGen.GetItemDrop_Chests`.
- Floating-island counts merge nearby detached cloud decorations (40-tile gaps) and deliberately do not rely only on Skyware Chests, because sky lakes have no chest. Living-tree fragments use a smaller 16-tile merge gap.
- The pre-Hardmode surface closure and 8×8 access path are explicitly estimators. Do not relabel them as exact simulation.
- Do not vendor Terraria/decompiled code or the unlicensed seed-analyzer research repository.
- Local portable SDK, research clones, generated worlds, `bin`, `obj`, and `artifacts` are intentionally ignored.
