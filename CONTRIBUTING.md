# Contributing to armux

Thanks for helping. armux is a Windows on ARM64 project. Most of this guide is about making sure your toolchain is native ARM64 rather than x64 under emulation.

## Ground rules

1. **No cmux code.** cmux is GPL-3.0-or-later and armux is MIT. Do not copy, paste or translate cmux source, assets or screenshots. Build from cmux's public docs and observed behavior. If you have read cmux's source for a feature, say so in the PR so a reviewer can check the result is an independent implementation.
2. **Every UI element traces to a cmux feature.** Link the cmux README or docs section in your PR. Windows-specific differences go in [ARCHITECTURE.md, section 9](ARCHITECTURE.md#9-windows-deviations).
3. **Memory is a feature.** UI or runtime changes include before and after numbers from `scripts/measure-memory.ps1`.
4. **Dependencies are rare.** Only MIT, Apache-2.0, BSD, Unicode-3.0 or Zlib licenses, including transitive dependencies. Add each one to the [dependency register](ARCHITECTURE.md#10-dependency-register) with a reason. Prefer the standard library or a few lines of code over a crate.
5. **ARM64 only.** No macOS, Linux or x64 targets.

## Setup

You need a Windows 11 ARM64 device. Check each tool is the **ARM64** build.

| Tool | Install | Check |
|---|---|---|
| Visual Studio 2022 or 2026 (Community or Build Tools) | Workload "Desktop development with C++", plus components **MSVC ARM64 build tools** and **Windows 11 SDK** | `link.exe` from `VC\Tools\MSVC\*\bin\Hostarm64\arm64` |
| Rust via rustup | Download `rustup-init.exe` for **aarch64** from [rustup.rs](https://rustup.rs) | `rustc -vV` shows `host: aarch64-pc-windows-msvc` |
| Git | [git-scm.com](https://git-scm.com) (ARM64 installer) | `git --version` |
| PowerShell 7 | `winget install Microsoft.PowerShell` | `$PSVersionTable.PSVersion` |
| Node.js LTS (Phase 2 onward) | ARM64 installer from [nodejs.org](https://nodejs.org) | `node -p process.arch` prints `arm64` |

The toolchain version is pinned in `rust-toolchain.toml`. rustup installs it on first build.

Then:

```powershell
git clone https://github.com/apaslam09/armux
cd armux
cargo fmt --check
cargo clippy --target aarch64-pc-windows-msvc -- -D warnings
cargo build --release --target aarch64-pc-windows-msvc
pwsh scripts/check-pe-arm64.ps1 target/aarch64-pc-windows-msvc/release/armux.exe
```

### Common ARM64 traps

- **`linker link.exe not found`, or `LNK1181: cannot open input file 'kernel32.lib'`.** Usually the Windows SDK is missing, even when the MSVC ARM64 tools are installed. rustc only finds `link.exe` automatically once the SDK is present. Check that `C:\Program Files (x86)\Windows Kits\10\Lib\<version>\um\arm64\kernel32.lib` exists.
- **Visual Studio says the SDK is installed but the folder is empty.** The installer's record is stale, so "Modify" does nothing. Install the standalone SDK instead: `winget install --id Microsoft.WindowsSDK.10.0.26100 -e`.
- **x64 Node.js.** An x64 Node under emulation installs x64 native modules, which then fail to load or run slowly. Check `node -p process.arch`.
- **x64 rustup host.** An x64 rustup builds x64 by default and runs the compiler under emulation. Reinstall with the aarch64 `rustup-init.exe`.
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
- Phase 2 onward: one UI component at a time, each mapped to a cmux feature.

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `ci:`, `refactor:`, `perf:`, `test:`, `chore:`. Add a scope where it helps: `feat(sidebar): show unread badge`.
- Keep PRs to one topic. Fill in the PR template.
- Never commit secrets, tokens or personal paths.
- CI must pass on `windows-11-arm` before review.

## Reporting bugs

Use the bug report form. Include your device, SoC, Windows build and confirm armux runs as ARM64 in Task Manager. For memory issues, paste the output of `scripts/measure-memory.ps1`.

## License

By contributing, you agree your contributions are licensed under the [MIT License](LICENSE).
