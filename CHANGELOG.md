# Changelog

## 1.3.0

### Resource protection

- Add a resource guard that reserves the minimum the roll needs to finish what it
  started. It samples free physical memory for the whole machine, not just this
  process, and stops launching new worlds below a configurable floor (default
  1536 MB, capped at a quarter of physical memory).
- Below the reserve line the guard parks the servers this roll owns so the
  analyser and the save path keep getting memory and CPU, then resumes them once
  memory returns. Suspensions are reference counted, so parallel workers cannot
  stack them.
- Reserve logical processors for the interface, analyser and save path (default
  1), so a four-core machine configured for parallelism 4 runs at most three
  servers.
- Cap each generated world through a Windows job object (`ServerMemoryLimitMb`,
  default 3072 MB) so one runaway server cannot drag the machine into paging.
- Add a no-output watchdog (`ServerStallTimeout`, default 3 minutes): a server
  that stops reporting progress is killed and failed instead of holding a
  concurrency slot until the per-world timeout. A slow but healthy world keeps
  reporting progress and is not affected.
- Raise the tool's own process priority to `AboveNormal` so it is not starved by
  the servers it starts, and apply the configured priority class to each server
  process rather than relying on the server's own handling.
- Report a resource summary (throttle count, parked servers, total wait) at the
  end of a run, and record a resource snapshot with every journalled attempt.

### Interruption protection

- Copy every accepted world out of the volatile `_work` directory into the
  session's `winners/staging/` as soon as it is analysed, and journal the staged
  path. An interrupted run now loses at most the worlds that were in flight.
- Add `session.journal.jsonl`, an append-only, per-attempt flushed journal that
  records both attempts that are in flight and attempts that finished.
- Add `session.state.json`, written atomically through a temporary file, with a
  session outcome. A session still marked `Running` was interrupted and can be
  continued.
- Add session resumption: `roll --resume` continues the most recent interrupted
  session, re-enumerates the same seed order from a persisted enumeration seed,
  skips seeds that already finished, and re-analyses only the winners that
  survive into the final report.
- Add `sessions` to list resumable sessions and `recover` to export the worlds an
  interrupted session had already finished without generating anything new.
- Refuse to resume a session whose world parameters no longer match, so worlds
  generated under different rules are never ranked together.
- Put every server in a `KILL_ON_JOB_CLOSE` job object and record live pids in
  `server-pids.txt`, so an interrupted run leaves no orphaned servers consuming
  memory and CPU; the next run clears them along with its stale `_work`
  directories.
- Write a truthful state file from a process-exit handler and a `crash.log` when
  the engine fails, instead of leaving a session that looks like it is still
  running.

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
