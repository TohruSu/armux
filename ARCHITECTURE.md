# armux architecture

Status: **Phase 2, stack selection.** Spike measurements are in section 6; the recommendation awaits maintainer approval. Sections marked _Phase 2_ or _Phase 3_ are plans. They will be replaced by measured results when those phases run. Nothing in this file claims a measured number unless it shows the command and date used to measure it.

## 1. Scope

armux is a terminal workspace app for **Windows 11 on ARM64**. It follows the UI and feature set of [cmux](https://github.com/manaflow-ai/cmux), the macOS app, as closely as Windows allows.

- Build target: `aarch64-pc-windows-msvc` only. Release binaries must have PE machine type `0xAA64` (`scripts/check-pe-arm64.ps1`, enforced in CI).
- Not in scope: macOS, Linux, x64, ARM64EC. x64 users already have other options (section 2).
- Resource efficiency is a product requirement. Memory is measured, budgeted and reported per scenario (section 7).

### Roadmap

| Phase | Deliverable | Exit gate |
|---|---|---|
| 1 | Repository, docs, CI on a native ARM64 runner | Maintainer review |
| 2 | Full cmux-style UI on mock data, stack comparison, memory measurements | Maintainer approves the UI and the memory numbers |
| 3 | ConPTY shells (pwsh, cmd, WSL), terminal rendering, OSC 9/99/777 notifications, real browser pane, `armux` CLI | Maintainer review |

## 2. Prior art and positioning

Checked 2026-10-25:

| Project | What it is | Windows ARM64 build |
|---|---|---|
| [manaflow-ai/cmux](https://github.com/manaflow-ai/cmux) | Native macOS terminal (Swift/AppKit, libghostty). GPL-3.0-or-later. | n/a (macOS only) |
| [kevmtt/wmux](https://github.com/kevmtt/wmux) | cmux port for Windows. Electron + xterm.js + node-pty. MIT. | No. README lists only `wmux-*-win-x64.zip`. |
| [openwong2kim/wmux](https://github.com/openwong2kim/wmux) | Cross-platform terminal multiplexer. Squirrel.Windows packaging. | None visible on the v4.10.0 release page (6 of 18 assets were not shown). |
| psmux | tmux-style CLI multiplexer | Not a GUI |

armux differs on three points: native ARM64 builds, cmux-level UI fidelity, and a measured memory budget. It does not try to replace the projects above on x64.

## 3. cmux license decision

- cmux is **GPL-3.0-or-later**. Its server components (`web/`, Cloudflare workers, relay services) are under **BSL 1.1**. Source: cmux README, read 2026-10-25.
- armux is **MIT**. MIT code cannot contain GPL-derived code without becoming GPL.
- **Decision: armux copies no cmux source code, assets, icons or screenshots.** It is reimplemented from cmux's public README, docs (cmux.com/docs) and observed behavior. Feature names, the concept hierarchy and keyboard shortcut layout are interface facts, not copied code.
- Contributors must not paste or translate cmux source (see CONTRIBUTING.md). Reviewers reject PRs that do.
- cmux screenshots are compared against armux screenshots locally. They are linked from our docs, never committed.
- kevmtt/wmux is MIT. Its code could legally be reused with attribution, but it is Electron/React, which this project avoids (section 6). Any reuse is recorded in section 10.

## 4. Concept model (from cmux)

armux uses cmux's hierarchy and names unchanged (cmux.com/docs/concepts):

```
Window                    one top-level window, own sidebar
└─ Workspace              a sidebar entry ("tab" in UI copy, "workspace" in CLI/API)
   └─ Pane                a split region (split right / split down)
      └─ Surface          a tab inside a pane, own tab bar, own ARMUX_SURFACE_ID
         └─ Panel         content: terminal or browser (internal, CLI addresses surfaces)
```

CLI handles follow cmux's short-ref style: `workspace:2`, `pane:1`, `surface:7`. A bare number is an index, not an ID.

Environment variables use the prefix `ARMUX_` (cmux uses `CMUX_`). See deviation D-05.

## 5. Feature traceability

Every UI element traces to a cmux feature. Windows deviations link to section 9.

| cmux feature (source) | armux element | Phase | Deviation |
|---|---|---|---|
| Vertical sidebar of workspaces showing git branch, PR status/number, working directory, listening ports, latest notification text (README) | Sidebar with one row per workspace, status line, unread badge | 2 (mock), 3 (live) | — |
| Sidebar unread badge and tab highlight when an agent needs attention (README) | Attention indicator on the workspace row | 2 | — |
| Per-pane tab bar of surfaces (docs/concepts) | Tab bar | 2 | — |
| Split right / split down, directional focus, resize, zoom, equalize (README, docs/keyboard-shortcuts) | Split panes, resizable dividers, zoom toggle | 2 | D-01 |
| Built-in browser split beside the terminal, address bar, back/forward/reload, dev tools (README) | Browser panel on WebView2 | 2 (shell), 3 (live) | D-02, D-03 |
| Command palette, `⌘⇧P`, `⌃N`/`⌃P` to move (docs/keyboard-shortcuts) | Command palette | 2 | D-01 |
| Blue notification ring on panes that need attention (README) | Pane ring | 2 | — |
| Notification panel `⌘I`, jump to latest unread `⌘⇧U` (README) | Notification panel | 2 | D-04 |
| Desktop notification when a process needs attention (README) | Windows toast | 3 | D-04 |
| OSC 9, OSC 99, OSC 777 detection (README) | OSC parser in terminal pipeline | 3 | — |
| `cmux notify` CLI, socket API (README) | `armux notify`, named-pipe API | 3 | D-06 |
| Themes from Ghostty config (README) | Theme tokens, dark and light | 2 | D-07 |
| Flash focused panel `⌘⇧H` (README) | Focus flash animation | 2 | — |
| Find, find in directory (README) | Find bar | 2 (UI), 3 (live) | — |
| SSH workspaces, Cloud workspaces, Claude Code Teams, browser import, session restore (README) | Not planned for Phases 1 to 3 | later | — |

## 6. Stack

### Candidates

| | A. Tauri 2 + xterm.js | B. Rust native (windows-rs, Direct2D/DirectWrite) | C. WinUI 3 (Windows App SDK) |
|---|---|---|---|
| UI layer | HTML/CSS in WebView2 | Own widgets drawn with Direct2D | XAML controls |
| Terminal | xterm.js (MIT) | `alacritty_terminal` (Apache-2.0) for VT state, own renderer | Own renderer or Windows Terminal control (MIT, hard to embed) |
| WebView2 at idle | **Always running** (it is the UI) | Not running until a browser panel opens | Not running until a browser panel opens |
| UI fidelity per dev hour | Highest: cmux's sidebar, rings and palette are CSS work | Lowest: layout, text input, IME, accessibility (UI Automation) by hand | Medium |
| ARM64 status | Rust and WebView2 both native ARM64 | Native ARM64 | Native ARM64 |

Electron is excluded. It ships its own Chromium, so its baseline sits above every candidate here.

Key tradeoff: with A, the WebView2 process group (browser, GPU, renderer, utility) is part of the idle baseline. With B or C, idle is lower, and the same WebView2 group starts only when the first browser panel opens. With the browser open, all three converge, because all three use WebView2 for the browser panel.

### Spike results (measured 2026-10-25)

**Setup.** Surface Laptop 7, Snapdragon X Plus, Qualcomm Adreno X1-85 GPU (driver 31.0.152.1), Windows 10.0.26200, WebView2 Runtime 155.0.4283.62, Rust 1.96.1, .NET SDK 8.0.426, Windows App Runtime 1.8. All spikes are ARM64 release builds (PE checked).

**Spikes.** Each spike draws the same 1280×800 window: a sidebar with 5 workspace rows, a tab label and 40 lines of static monospace text. An optional browser panel shows the same fixed local HTML page in every spike. None of them has a terminal engine yet; that cost lands in Phase 3 for every stack alike. The spikes were throwaway code and are not in the repo.

- A: Tauri 2.12, UI as HTML in a child webview, browser as a second child webview (`unstable` feature).
- B: `windows` 0.62 + `webview2-com` 0.39, Win32 window, Direct2D/DirectWrite drawing.
- C: WinUI 3 (Windows App SDK 1.8), C#/.NET 8, framework-dependent, code-only UI, `WebView2` control.

**Method.** `scripts/bench.ps1`. Start time is the median of 10 cold starts (after 1 discarded warm-up), from process creation to first presented frame. For B that is the first `EndDraw`, for A the UI page-load event, for C the first `CompositionTarget.Rendering`. Memory is app-attributable (section 7), the median of 5 samples after a 12 s settle. For "browser closed", the panel opens at launch, closes at 5 s, and memory is sampled at 70 s.

Working set / private bytes in MB:

| Scenario | B native, D2D software | B native, D2D hardware | A Tauri 2 | C WinUI 3 |
|---|---|---|---|---|
| Idle, browser never opened | **29.7 / 14.6** | 52.3 / 91.4 | 344.9 / 130.8 | 112.5 / 48.4 |
| Browser panel open | 358.1 / 138.4 | not run | 416.8 / 158.3 | 441.5 / 174.3 |
| Browser closed, 65 s later | 36.7–40.0 / 15.6–15.9 (2 runs) | not run | 370.7 / 132.4 | 121.5 / 50.0 |
| Same, after working-set trim | 13.3 / 15.6 | — | — | — |
| Processes idle / browser open | 1 / 7 | 1 / — | 7 / 8 | 1 / 7 |
| Cold start, median of 10 | **82 ms** | 116 ms | 407 ms | 325 ms |
| Shipped size | 226 KB exe | 226 KB exe | 3,089 KB exe | 130 KB exe + 37.2 MB folder, plus .NET 8 and Windows App Runtime installed separately |
| Runtime crates / license status | 11, all MIT or Apache-2.0 | same | 128; MPL-2.0 `option-ext` at runtime (not allowed) | Windows App SDK under Microsoft Software License Terms (not allowed) |

**Findings.**

1. **The GPU driver is a fixed cost.** Creating a Direct2D hardware render target (a D3D device on the Adreno X1-85) adds about 23 MB working set, 77 MB private and 34 ms of startup to B. Every GPU-composited UI pays this. C pays it in-process. WebView2 pays it in its own GPU process: always for A, and for B and C only while a browser panel is open.
2. **WebView2 is the largest cost.** The first browser panel adds 318–329 MB working set and 122–126 MB private in 6 processes for B and C. A pays most of that at idle, because its UI *is* a WebView2. A's second webview adds only one renderer (+54.9 / +18.7 MB).
3. **Closing works for B and C.** Releasing the controller and environment makes every WebView2 process exit. In A the UI webview keeps the group alive.
4. **Retained memory after close is mostly mapped pages.** B keeps +7% private and +24–35% working set after the browser closes. A working-set trim (`SetProcessWorkingSetSize(-1, -1)`) brings working set to 13.3 MB, below idle, but private bytes don't move. So private bytes is the honest retained-cost metric.
5. **The WebView2 user data folder needs a short path.** C's default folder, next to the exe under a ~200-character path, failed with `0x80080005` (`CO_E_SERVER_EXEC_FAILURE`) a few seconds after start. Under `%LOCALAPPDATA%` it worked. armux puts it in `%LOCALAPPDATA%\armux\WebView2`.

### Recommendation: B, Rust native

Proposed for approval, not final until the maintainer signs off.

- **Why B.** It is the only candidate that meets the idle budget (29.7 / 14.6 MB against ≤ 100 / ≤ 60), and it starts fastest (82 ms) with one process and a clean license tree. Its WebView2 cost appears only while a browser panel is open, and the panel can be closed to give memory back.
- **Renderer.** Software Direct2D by default, with a hardware renderer behind a setting. Hardware rendering breaks the private-bytes budget on this GPU driver (91.4 MB). The risk is CPU cost for full-screen redraws at high refresh rates, so Phase 3 measures CPU use while scrolling. If software rendering is too expensive, hardware becomes the default and the private target is revised with the driver cost stated.
- **Costs of B.** All UI is built by hand: layout, hit testing, focus, text input and IME (palette, rename), animations, and accessibility through UI Automation. This is the main schedule risk. It is kept small by a minimal retained widget layer written for exactly the cmux components in section 5, with no general-purpose toolkit.
- **Why not A.** Idle is 3.4× the working-set target and 2.2× the private target. WebView2's browser, GPU and renderer processes alone exceed the private target. It also ships an MPL-2.0 crate, which isn't on the allowlist.
- **Why not C.** Idle working set misses the target by 12.5%. The Windows App SDK license isn't on the allowlist, and users need the .NET and Windows App runtimes installed.

### Toolchain (Phase 1)

- Rust 1.96.1, pinned in `rust-toolchain.toml`, host and target `aarch64-pc-windows-msvc`.
- MSVC ARM64 build tools and the Windows 11 SDK (link needs `kernel32.lib` etc.).
- Release profile: `opt-level = "s"`, thin LTO, one codegen unit, `panic = "abort"`, stripped. Smaller images map fewer pages, which lowers working set.

## 7. Memory budget

### Method

`scripts/measure-memory.ps1 -Name armux` walks the process tree from `armux.exe` by `ParentProcessId` (Win32_Process, with a creation-time check against PID reuse). For each process it reads `WorkingSet64` and `PrivateMemorySize64` and sums them. It takes 5 samples 1 s apart and reports the median.

Processes are grouped:

| Category | Processes | Counts against budget |
|---|---|---|
| app | `armux.exe` and helpers | yes |
| webview | `msedgewebview2.exe` | yes |
| conpty | `OpenConsole.exe`, `conhost.exe` | yes |
| shell | `pwsh`, `powershell`, `cmd`, `wsl`, `wslhost`, `bash` | reported, not budgeted |
| workload | anything a shell started | reported, not budgeted |

**app-attributable** = app + webview + conpty. Shells are excluded from the budget because pwsh costs the same in any terminal, but they are always reported, so the total is visible.

Measurements run after a 10 s settle, with battery saver off and the window visible. The device and Windows build are recorded with each result.

### Reference points (not a benchmark)

Measured 2026-10-25 on the maintainer's Surface Laptop 7 (Snapdragon X Plus, Windows 10.0.26200), one sample set while in use:

| Process | Working set (MB) | Private (MB) |
|---|---|---|
| `OpenConsole.exe` (one ConPTY host) | 13.8 | 3.7 |
| `pwsh.exe` 7.6 (idle shell) | 105.0 | 38.0 |
| Warp (Rust-native terminal), app processes, 1 session | 289.6 | 474.4 |

These show the per-pane cost floor (a ConPTY host) and why shells are reported separately: one pwsh exceeds the whole app budget below.

### Targets (proposed, app-attributable, summed)

| Scenario | Working set | Private bytes | Reason |
|---|---|---|---|
| Idle: 1 workspace, 1 terminal pane, browser closed | **≤ 100 MB** | **≤ 60 MB** | Roughly a third of the Warp reference above. Low enough to rule out Electron. Candidate A must prove a single-renderer WebView2 UI fits; B and C should fit with room to spare. |
| Each additional terminal pane | **≤ 20 MB** | **≤ 8 MB** | One ConPTY host measures 13.8 / 3.7 MB. The rest covers the terminal buffer (10k lines) and renderer state. |
| First browser panel open (fixed local test page) | reported | reported | Page-dependent. Reported separately, open and closed. |
| All browser panels closed, after 60 s | **≤ idle + 10%** | **≤ idle + 10%** | Closing the browser must return memory. |

These targets are proposals. After the Phase 2 spikes they are either confirmed or revised in this file, with the reason. A missed target is reported with its cause, never hidden.

### Status against the spikes (2026-10-25, recommended stack B with software rendering)

| Target | Measured | Status |
|---|---|---|
| Idle ≤ 100 / ≤ 60 MB | 29.7 / 14.6 MB | Met |
| Each additional terminal pane ≤ 20 / ≤ 8 MB | not measured; the spikes have no panes | Open (measured with the Phase 2 UI and again with ConPTY in Phase 3) |
| Browser closed ≤ idle + 10% | private +7% (15.6 vs 14.6); working set +24–35% (36.7–40.0 vs 29.7) | **Private met, working set missed** |

Cause of the working-set miss: after WebView2 shuts down, its loader and the system DLLs it touched stay mapped in armux's process. A working-set trim brings working set to 13.3 MB, but that only moves pages to the standby list. **Proposed revision:** judge the browser-closed target on private bytes and keep reporting working set. Pending maintainer approval.

### Browser panel policy (_Phase 2/3_)

The browser panel is the largest memory risk, since each WebView2 environment adds its own process group.

1. **Lazy.** No WebView2 environment exists until the first browser panel opens.
2. **One shared environment.** All browser panels share one `CoreWebView2Environment` (one browser and one GPU process; each panel adds a renderer).
3. **Suspend when hidden.** A browser panel that is not visible calls `TrySuspend` and sets `MemoryUsageTargetLevel = Low`. It resumes on show.
4. **Cap.** At most 2 live browser panels by default. Opening a third discards the least recently used one, keeping its URL so it reloads on focus.
5. **Tear down.** When the last browser panel closes, the environment is released after 30 s, so the WebView2 processes exit (verified in spike B). armux then trims its own working set once.
6. **Short data path.** The user data folder is `%LOCALAPPDATA%\armux\WebView2`. A long default path broke WebView2 start-up in spike C (section 6, finding 5).
7. **Measure** open, hidden (suspended) and closed, every release.

## 8. Keyboard shortcuts (_draft, finalized in Phase 2_)

cmux binds ⌘, which Windows doesn't have. Plain Ctrl+letter can't be used, because terminals need it (Ctrl+C, Ctrl+D, Ctrl+W, Ctrl+K, Ctrl+L, ...). armux maps:

- ⌘ to **Ctrl+Shift**
- ⌘⇧ to **Ctrl+Alt+Shift**
- ⌥⌘ to **Ctrl+Alt**
- ⌃⌘ to **Alt+Shift**

Exceptions are marked with *.

| Action | cmux | armux |
|---|---|---|
| New workspace | ⌘N | Ctrl+Shift+N |
| Jump to workspace 1 to 8 / last | ⌘1–8 / ⌘9 | Ctrl+Shift+1–8 / Ctrl+Shift+9 |
| Next / previous workspace | ⌃⌘] / ⌃⌘[ | Alt+Shift+] / Alt+Shift+[ |
| Close workspace | ⌘⇧W | Ctrl+Alt+Shift+W |
| Rename workspace | ⌘⇧R | Ctrl+Alt+Shift+R |
| Toggle sidebar | ⌘B | Ctrl+Shift+B |
| New surface | ⌘T | Ctrl+Shift+T |
| Close surface | ⌘W | Ctrl+Shift+W |
| Next / previous surface | ⌃Tab / ⌃⇧Tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Jump to surface 1 to 8 | ⌃1–8 | Alt+1–8 * (Ctrl+digit sends legacy control codes) |
| Split right / down | ⌘D / ⌘⇧D | Ctrl+Shift+D / Ctrl+Alt+Shift+D |
| Focus pane | ⌥⌘ arrows | Ctrl+Alt+arrows |
| Resize pane | ⌃⇧ H J K L | Alt+Shift+arrows * (Ctrl+Shift+K/L are taken by ⌘K/⌘L) |
| Toggle pane zoom | ⌘⇧↩ | Ctrl+Alt+Shift+Enter |
| Equalize splits | ⌃⌘⇧= | Ctrl+Alt+= * |
| Flash focused panel | ⌘⇧H | Ctrl+Alt+Shift+H |
| Command palette | ⌘⇧P | Ctrl+Shift+P * (matches Windows Terminal and VS Code) |
| Palette next / previous | ⌃N / ⌃P | Ctrl+N / Ctrl+P (palette has focus) |
| Open browser in split | ⌘⇧L | Ctrl+Alt+Shift+L |
| Browser: address bar, back, forward, reload, dev tools | ⌘L, ⌘[, ⌘], ⌘R, ⌥⌘I | Ctrl+L, Alt+Left, Alt+Right, F5, F12 * (Edge defaults, see D-03) |
| Notifications panel | ⌘I | Ctrl+Shift+I |
| Jump to latest unread | ⌘⇧U | Ctrl+Alt+Shift+U |
| Toggle unread | ⌥⌘U | Ctrl+Alt+U |
| Find | ⌘F | Ctrl+Shift+F |
| Clear scrollback | ⌘K | Ctrl+Shift+K |
| Copy / paste | ⌘C / ⌘V | Ctrl+Shift+C / Ctrl+Shift+V; Ctrl+C copies when text is selected |
| Font size + / − / reset | ⌘+ / ⌘- / ⌘0 | Ctrl+= / Ctrl+- / Ctrl+0 |
| New window | ⌘⇧N | Ctrl+Alt+Shift+N |
| Settings | ⌘, | Ctrl+, |
| Quit | ⌘Q | Alt+F4 |

## 9. Windows deviations

| ID | cmux behavior | armux behavior | Reason |
|---|---|---|---|
| D-01 | ⌘-based shortcuts | Ctrl+Shift-based mapping (section 8) | No ⌘ key. Ctrl+letter belongs to the terminal. On layouts with AltGr (Ctrl+Alt), a Ctrl+Alt binding that produces a character on the active layout passes through to the terminal. |
| D-02 | WebKit web view | WebView2 (Chromium) | The system web engine on Windows. Preinstalled on Windows 11. |
| D-03 | Browser shortcuts follow Safari defaults | Browser shortcuts follow Edge defaults | Same principle as cmux: follow the platform browser. No terminal conflict inside a browser panel. |
| D-04 | macOS notification center | Windows toast notifications | Platform API. |
| D-05 | `CMUX_*` environment variables | `ARMUX_*` | Avoid confusing tools that detect cmux. |
| D-06 | Unix socket API | Named pipe `\\.\pipe\armux-<user>` | Windows-native IPC. |
| D-07 | Reads Ghostty config for themes | Own theme file with dark and light tokens | Ghostty has no Windows build. Ghostty theme import may come later. |

## 10. Dependency register

Every dependency is listed here with its license and why it is needed. Allowed licenses, direct and transitive: MIT, Apache-2.0, BSD (any clause count), Unicode-3.0 and Zlib. Unicode-3.0 and Zlib were added on 2026-10-25: both are permissive with no copyleft, and Unicode-3.0 appears in `unicode-ident`, which nearly every Rust proc-macro depends on. Anything else, including MPL-2.0 and proprietary SDK terms, stops for maintainer review.

Check a dependency tree before adding it: `cargo metadata --format-version 1 --filter-platform aarch64-pc-windows-msvc`, then group the packages by `license`.

| Dependency | Where | License | Why |
|---|---|---|---|
| `actions/checkout@v7` | CI | MIT | Check out the repo |
| `actions/cache@v6` | CI | MIT | Cache cargo registry and `target/` |
| `actions/upload-artifact@v7` | CI | MIT | Publish the ARM64 build |

The Rust crate currently has zero dependencies.
