//! armux entry point.
//!
//! Phase 1 placeholder: proves the aarch64-pc-windows-msvc build and CI pipeline.
//! The UI shell lands in Phase 2 and the CLI (`armux notify`, ...) in Phase 3.

fn main() {
    match std::env::args().nth(1).as_deref() {
        Some("--version" | "-V") => println!("armux {}", env!("CARGO_PKG_VERSION")),
        _ => {
            println!(
                "armux {} ({})",
                env!("CARGO_PKG_VERSION"),
                std::env::consts::ARCH
            );
            println!("UI not implemented yet. See ARCHITECTURE.md for the roadmap.");
        }
    }
}
