# Changelog

## 1.2.0

### GUI rebuilt from scratch

- Replace the entire window with a hand-painted dark interface. The system
  `TabControl`, `DataGridView`, `ComboBox`, `CheckBox` and scrollbars are gone,
  so no control can ever fall back to the light OS theme. Every surface, glyph
  and scrollbar is drawn by the application from one palette.
- Fix the window sizing bug that shrank the window to roughly two thirds of its
  intended size on a scaled display, leaving it smaller than its own minimum
  size. The window is now sized from the real monitor DPI, fits the working
  area, and supports per-monitor DPI changes.
- Fix the split layout that let the settings panel consume most of the window
  and squeezed the results area into a clipped strip.
- Replace the misaligned settings grid with a deterministic top-down stack, so
  captions and fields can no longer drift out of alignment or overlap.
- Add a headless layout audit (`--ui-audit`) that fails on clipped children,
  overlapping siblings, zero-sized controls and insufficient palette contrast.
- Add a rendered-text contrast audit over screenshots that verifies every drawn
  text colour against the surface it actually sits on. The weakest pair in the
  shipped UI is 4.67:1; most are above 9:1.
- Fix unpainted control backgrounds that rendered as black stripes between rows.
- Replace the flat result grid with a ranked table, an interactive world
  overview map (zoom, pan, fit) with a legend, and a grouped detail panel.
- Add a real log view with severity colouring, plus copy and open-folder actions.
- Add a colour-coded special-seed selector, a filter preset picker with per-row
  criterion editing, and a themed add/edit criterion dialog.
- Add a generated multi-resolution application icon drawn from the same vector
  artwork as the in-app icons.
- Add a tooltip-free, keyboard-navigable interface: every custom control
  supports focus, Enter/Space activation and arrow-key navigation.

## 1.1.0

- Replace the fixed post-generation wait with exact `.wld` section/footer validation, reducing per-world overhead by about 1.4 seconds.
- Throttle server progress relay to 10% buckets and expose generation/analysis timings in GUI and CLI results.
- Add balanced, fastest, low-impact and idle TerrariaServer resource strategies; balanced single-process generation is now the default.
- Rework access scoring to follow the lowest-cost route and account for vertical work, unsupported sky travel, digging, liquids, jungle, evil, snow, surface/underground desert, spider caves, hives, dungeon, temple and progression locks.
- Add route-cost metrics for life crystals, dungeon, jungle, snow, desert, Shimmer and Jungle Temple.
- Avoid a redundant full-world life-crystal scan, use precomputed tile classifications, and stop Dijkstra once all real targets are finalized.

## 1.0.0

- Initial Windows GUI/CLI release using vanilla TerrariaServer generation and read-only `.wld` analysis.
