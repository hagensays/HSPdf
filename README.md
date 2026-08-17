# HSSuite

Canonical template and design standard for small HS Windows desktop utilities.

HSSuite is deliberately **not** a monolithic application and not a shared runtime dependency. New tools are created as independent repositories from this template, then keep their own source, version history, CI and releases.

## Intended environment

- Windows 10 Enterprise/LTSC-class office machines
- WPF on .NET Framework 4.7.2
- no WinUI 3 requirement
- no NuGet/runtime dependencies by default
- self-contained application folder
- generated output defaults to the folder containing the running EXE
- generated files must not overwrite existing files

## New app flow

1. Create a new repository from this GitHub template.
2. Run `scripts/Initialize-App.ps1 -AppName HSWhatever -Description "..."`, or have the coding agent perform the equivalent rename.
3. Read `AGENTS.md`, `SUITE_STANDARD.md` and `DESIGN_SYSTEM.md` before implementation.
4. Build the actual product on a semantic-version branch such as `v0.1.0`.
5. PR → CI → merge → automatic release → verification → cleanup.

## Template contents

- a compilable WPF starter shell
- canonical colors, spacing and control styles
- standard header, workspace and status bar
- local-output/non-overwrite helper
- universal suite rules
- CI and release workflows
- template validation checks
- initialization script

## Important distinction

Universal suite rules belong here. Product-specific rules belong in the individual application repo. For example, HSScanner can be read-only while a future HSRenamer is allowed to rename files.

See `TEMPLATE_USAGE.md` for the exact creation workflow.
