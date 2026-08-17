# Development Workflow

This repository uses:

branch → implementation → PR → CI → merge → build → release → verification

## Rules for Coding Agents

For every code change:

1. Read the current `AGENTS.md` first.
2. Read `SUITE_STANDARD.md` and `DESIGN_SYSTEM.md` before changing user-facing UI.
3. Treat current `main` as the authoritative source.
4. Never develop or commit directly on `main`.
5. Create a version branch from current `main` named exactly `vX.Y.Z`.
6. Implement the requested change on that branch.
7. Open a PR from `vX.Y.Z` into `main`.
8. Run CI on the PR.
9. Never bypass, disable or weaken failing CI.
10. If CI fails, inspect the actual failure, fix it on the branch and rerun CI.
11. Merge only after required CI checks pass.
12. After merge, automatically create/tag `vX.Y.Z`, build the release, create a GitHub Release, attach compiled artifacts, include short release notes, verify assets and delete the version branch.
13. A task is not finished until `branch → change → PR → green CI → merge → release → verification → cleanup` is complete.

## Suite Behaviour

- Preserve the HSSuite visual language unless a product requirement genuinely requires an exception.
- Use WPF and .NET Framework 4.7.2 by default for compatibility with the target office environment.
- Do not add NuGet packages, external runtimes, installers or framework dependencies unless the user explicitly approves the exception.
- Keep each application independently buildable and releasable.
- Do not introduce a shared HSSuite DLL. Reuse the template source instead.
- App-generated outputs default to the executable directory and must use non-overwriting names.
- Do not use `%TEMP%`, `%APPDATA%` or `%LOCALAPPDATA%` for application state/output by default.
- Product-specific source-file mutations are neither universally forbidden nor universally allowed; define them explicitly in the product repo.
- Keep changes focused on the requested task.
- Do not claim runtime testing when only compilation or CI testing occurred.
- If an important product/design decision is genuinely ambiguous, ask before choosing it.

## Naming

- Product repositories and executables should normally use the `HS` prefix: `HSScanner`, `HSRenamer`, `HSCompare`, etc.
- Release branches: `vMAJOR.MINOR.PATCH`.
- Public release asset: `<AppName>-vMAJOR.MINOR.PATCH.exe`.
