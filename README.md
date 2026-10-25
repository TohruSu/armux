# armux

A terminal workspace app for **Windows 11 on ARM64**, modeled on [cmux](https://github.com/manaflow-ai/cmux) for macOS: vertical workspace sidebar, split panes, a browser beside your terminal, and notification rings when a coding agent needs you.

[![ci](https://github.com/apaslam09/armux/actions/workflows/ci.yml/badge.svg)](https://github.com/apaslam09/armux/actions/workflows/ci.yml)

> **Status: Phase 1 (repository setup).** No usable app yet. The UI arrives in Phase 2 and real shells in Phase 3. See the [roadmap](ARCHITECTURE.md#roadmap).

## Why

- **Native ARM64.** Built for `aarch64-pc-windows-msvc`. CI checks that every release binary has PE machine type `0xAA64`, so it never runs under x64 emulation.
- **cmux's UI and model.** The same Window > Workspace > Pane > Surface hierarchy, the same features and a shortcut layout mapped to Windows. Every UI element traces to a cmux feature ([traceability table](ARCHITECTURE.md#5-feature-traceability)).
- **Measured memory.** Memory has a budget per scenario, and the measurement script is in the repo. Numbers get published with each phase, including the ones that miss.

## Planned features

| Feature | Phase |
|---|---|
| Vertical sidebar of workspaces with status and unread badges | 2 |
| Per-pane tab bar, split right / down, resize, zoom | 2 |
| Command palette (`Ctrl+Shift+P`) | 2 |
| Notification rings and notification panel | 2 |
| Dark and light themes | 2 |
| Built-in browser panel (WebView2), loaded lazily and suspended when hidden | 2 to 3 |
| PowerShell 7, cmd and WSL through ConPTY | 3 |
| OSC 9 / 99 / 777 notification detection | 3 |
| `armux notify` CLI | 3 |

## Requirements

- Windows 11 on ARM64 (developed on a Surface Laptop 7, Snapdragon X Plus)
- WebView2 Runtime (preinstalled on Windows 11)

## Build from source

See [CONTRIBUTING.md](CONTRIBUTING.md#setup) for the full setup. In short:

```powershell
git clone https://github.com/apaslam09/armux
cd armux
cargo build --release --target aarch64-pc-windows-msvc
pwsh scripts/check-pe-arm64.ps1 target/aarch64-pc-windows-msvc/release/armux.exe
```

## Measuring memory

```powershell
pwsh scripts/measure-memory.ps1 -Name armux
```

This sums working set and private bytes across `armux.exe` and every process it starts (WebView2, ConPTY hosts, shells). It reports app cost and shell cost separately. The method and budget are in [ARCHITECTURE.md](ARCHITECTURE.md#7-memory-budget).

## Relationship to cmux

armux is an independent project, not affiliated with Manaflow. cmux is GPL-3.0-or-later and armux is MIT, so armux contains **no cmux source code**. It is reimplemented from cmux's public docs and observed behavior. See [ARCHITECTURE.md](ARCHITECTURE.md#3-cmux-license-decision).

## Contributing

Contributions are welcome, especially from people on Windows on ARM hardware. Start with [CONTRIBUTING.md](CONTRIBUTING.md) and issues labeled [`good first issue`](https://github.com/apaslam09/armux/labels/good%20first%20issue). No ARM64 device? CI builds and checks every PR on a native ARM64 runner.

## License

[MIT](LICENSE)
