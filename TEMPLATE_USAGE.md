# Creating a New HSSuite App

## Recommended method

Use GitHub's template-repository feature on `HSSuite` and create a fresh repository for each application.

Do **not** fork HSSuite and do not place multiple products in this repository.

## Bootstrap

After creating the new repository:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Initialize-App.ps1 -AppName HSFolderCompare -Description "Compare two folder trees and export differences."
```

The script renames the solution/project folder and replaces the starter identity throughout text files. Review the resulting diff before committing.

If an AI agent is doing the work, it may perform the equivalent transformation directly instead of executing the script.

## Then customize

1. Keep `Themes/Colors.xaml` and `Themes/Controls.xaml` as the canonical visual baseline.
2. Replace the starter workspace inside `MainWindow.xaml` with the product UI.
3. Keep the suite header and status bar unless there is a concrete reason not to.
4. Extend the product's `AGENTS.md` with product-specific safety rules.
5. Do not delete universal rules merely because the product does not currently exercise them.
6. Start the first product implementation on `v0.1.0`.

## Before first release

- update README with product purpose and safety model
- define what the tool may read/change/write
- ensure long-running operations do not freeze the UI
- verify output names are non-overwriting
- confirm CI builds the real Release EXE
- test on a representative Windows 10 machine before calling it production-verified

## Keeping apps visually aligned later

HSSuite is the source of truth for **new** apps. Existing apps do not automatically receive template changes. When the design standard changes materially, port the relevant source changes deliberately to each product on its own version branch. This prevents a suite-wide shared dependency from breaking production tools.
