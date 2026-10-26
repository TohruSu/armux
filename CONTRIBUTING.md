# Contributing to armux

Thanks for helping. armux is a Windows on ARM64 project. Most of this guide is about making sure your toolchain is native ARM64 rather than x64 under emulation.

## Ground rules

1. **No cmux code.** cmux is GPL-3.0-or-later and armux is MIT. Do not copy, paste or translate cmux source, assets or screenshots. Build from cmux's public docs and observed behavior. If you have read cmux's source for a feature, say so in the PR so a reviewer can check the result is an independent implementation.
2. **Every UI element traces to a cmux feature.** Link the cmux README or docs section in your PR. Windows-specific differences go in [ARCHITECTURE.md, section 9](ARCHITECTURE.md#9-windows-deviations).
3. **Memory is a feature.** UI or runtime changes include before and after numbers from `scripts/measure-memory.ps1`.
4. **Dependencies are rare.** Only MIT, Apache-2.0, BSD, Unicode-3.0 or Zlib licenses, including transitive dependencies. Bundled fonts may also be SIL OFL-1.1, shipped with their `OFL.txt`. Add each one to the [dependency register](ARCHITECTURE.md#10-dependency-register) with a reason. Prefer the base class library or a few lines of code over a package.
5. **ARM64 only.** No macOS, Linux or x64 targets.

## Setup

You need a Windows 11 ARM64 device. Check each tool is the **ARM64** build.

| Tool | Install | Check |
|---|---|---|
| .NET 10 SDK (ARM64) | `winget install Microsoft.DotNet.SDK.10` | `dotnet --info` shows `RID: win-arm64` |
| Windows App Runtime 1.8 | Usually already present on Windows 11; otherwise install the 1.8 runtime from Microsoft | `Get-AppxPackage *WindowsAppRuntime.1.8*` lists an Arm64 package |
| Visual Studio 2022 or 2026 (Community or Build Tools) | Workload "Desktop development with C++", plus **MSVC ARM64 build tools** and **Windows 11 SDK**. Needed for the NativeAOT release build only. | `C:\Program Files (x86)\Windows Kits\10\Lib\<version>\um\arm64\kernel32.lib` exists |
| Git | [git-scm.com](https://git-scm.com) (ARM64 installer) | `git --version` |
| PowerShell 7 | `winget install Microsoft.PowerShell` | `$PSVersionTable.PSVersion` |

The SDK version is pinned by `global.json`, and `nuget.config` points at nuget.org.

Then:

```powershell
git clone https://github.com/apaslam09/armux
cd armux
dotnet test --project tests/Armux.Core.Tests
dotnet build src/Armux.App -c Release -p:Platform=ARM64     # fast JIT build for development
src\Armux.App\bin\ARM64\Release\net10.0-windows10.0.26100.0\win-arm64\ArmuxApp.exe

# Release build (NativeAOT), the one memory numbers are reported for:
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
dotnet publish src/Armux.App -c Release -r win-arm64 -p:Platform=ARM64 -o publish
dotnet publish src/Armux.Cli -c Release -r win-arm64 -o publish
pwsh scripts/check-pe-arm64.ps1 publish/ArmuxApp.exe publish/armux.exe
```

Useful launch flags for UI work: `--scenario live|demo|idle|panes|browser` (`live`, the default, starts real shells; the others use mock terminals), `--panes N`, `--theme light`, `--palette`, `--notifications`, `--zoom`, `--select N`, `--explorer`, `--settings`, `--prompt-editor` (see `src/Armux.App/LaunchOptions.cs`). Set `ARMUX_SETTINGS_FILE` to a scratch path so screenshots and measurements use default fonts, not your own settings. `scripts/screenshot.ps1` captures the window, and `scripts/bench.ps1` measures start-up and memory.

`screenshot.ps1 -Keys` and `-Clicks` send real input. They check that armux is the foreground window before each key or click and stop otherwise, but run them only when you are not using the machine. For repeated actions, prefer UI Automation (icon buttons carry automation names), which sends no input to other windows.

To drive live terminals in tests, use the CLI instead of keystrokes: `armux send --surface N "command\n"` types into tab N, and `armux list` shows the ids. To debug rendering, set `ARMUX_PTY_LOG` to a file path before starting armux; it records the raw ConPTY output and every resize.

### Common ARM64 traps

- **`LNK1181: cannot open input file 'kernel32.lib'` during `dotnet publish`.** The Windows SDK is missing, even when the MSVC ARM64 tools are installed. Check that `C:\Program Files (x86)\Windows Kits\10\Lib\<version>\um\arm64\kernel32.lib` exists.
- **Visual Studio says the SDK is installed but the folder is empty.** The installer's record is stale, so "Modify" does nothing. Install the standalone SDK instead: `winget install --id Microsoft.WindowsSDK.10.0.26100 -e`.
- **`'vswhere.exe' is not recognized` during `dotnet publish`.** The NativeAOT linker step finds MSVC through vswhere. Put `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on `PATH` first, as shown above.
- **`NETSDK1102` when publishing.** NativeAOT needs a self-contained publish. The project sets this automatically for `dotnet publish`; don't pass `--self-contained false`.
- **`dotnet test` fails with "Testing with VSTest target is no longer supported".** Use `dotnet test --project ...`; `global.json` opts in to Microsoft.Testing.Platform.
- **`Armux.Core.dll` reports machine `0x014C`.** It's an IL-only (AnyCPU) library and runs as ARM64. Only the AOT `ArmuxApp.exe` is checked.
- **ARM64EC is not ARM64.** ARM64EC binaries report machine type `0x8664`. The PE check rejects them on purpose.
- **Task Manager** can confirm a running process: Details tab, add the Architecture column.

### No ARM64 device?

You can still contribute. Push to your fork and open a PR. CI builds, lints, tests and checks the PE machine type on a native `windows-11-arm` runner. Docs and CI work need no device at all.

## Finding a first issue

Issues labeled [`good first issue`](https://github.com/apaslam09/armux/labels/good%20first%20issue) name the files to read, define "done" and explain how to verify the change without ARM64 hardware where possible. Comment on the issue before starting so two people don't do the same work.

Good areas for a first contribution:

- Docs: setup steps for other ARM64 devices (Snapdragon X Elite, Windows Dev Kit 2023, Parallels on Apple silicon).
- `scripts/measure-memory.ps1`: more process categories, CSV output, comparing two runs.
- CI: caching, artifact naming, job summaries.
- UI: one component at a time, each mapped to a cmux feature (see the traceability table in ARCHITECTURE.md).

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `ci:`, `refactor:`, `perf:`, `test:`, `chore:`. Add a scope where it helps: `feat(sidebar): show unread badge`.
- Keep PRs to one topic. Fill in the PR template.
- Never commit secrets, tokens or personal paths.
- CI must pass on `windows-11-arm` before review.

## Reporting bugs

Use the bug report form. Include your device, SoC, Windows build and confirm armux runs as ARM64 in Task Manager. For memory issues, paste the output of `scripts/measure-memory.ps1`.

## License

By contributing, you agree your contributions are licensed under the [MIT License](LICENSE).
