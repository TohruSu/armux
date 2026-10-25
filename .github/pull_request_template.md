## What and why

<!-- One paragraph. Link the issue: "Closes #123". -->

## cmux traceability

<!-- Which cmux feature does this implement or change? "n/a" for non-UI work. Note any Windows deviation and update ARCHITECTURE.md if needed. -->

## Checklist

- [ ] Title follows [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, ...)
- [ ] `cargo fmt --check` and `cargo clippy -- -D warnings` pass
- [ ] No code copied from cmux (GPL-3.0-or-later). See CONTRIBUTING.md
- [ ] New dependencies (including transitive ones) are MIT, Apache-2.0, BSD, Unicode-3.0 or Zlib, and are listed with a reason in ARCHITECTURE.md
- [ ] UI or runtime change: memory measured before and after with `scripts/measure-memory.ps1` (paste below)

## Memory (if applicable)

```text
before:
after:
```
