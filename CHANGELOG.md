# Changelog

## 1.1.0

- Replace the fixed post-generation wait with exact `.wld` section/footer validation, reducing per-world overhead by about 1.4 seconds.
- Throttle server progress relay to 10% buckets and expose generation/analysis timings in GUI and CLI results.
- Add balanced, fastest, low-impact and idle TerrariaServer resource strategies; balanced single-process generation is now the default.
- Rework access scoring to follow the lowest-cost route and account for vertical work, unsupported sky travel, digging, liquids, jungle, evil, snow, surface/underground desert, spider caves, hives, dungeon, temple and progression locks.
- Add route-cost metrics for life crystals, dungeon, jungle, snow, desert, Shimmer and Jungle Temple.
- Avoid a redundant full-world life-crystal scan, use precomputed tile classifications, and stop Dijkstra once all real targets are finalized.

## 1.0.0

- Initial Windows GUI/CLI release using vanilla TerrariaServer generation and read-only `.wld` analysis.
