# HSSuite Application Standard

## 1. Architecture

HSSuite is a family of small, independent Windows applications. Each application gets its own repository, executable, versioning, CI and releases. HSSuite provides source-level consistency, not a shared runtime.

## 2. Compatibility baseline

- WPF desktop application
- .NET Framework 4.7.2
- AnyCPU unless a product has a concrete architecture requirement
- Windows 10 1809-era APIs only unless explicitly justified
- no WinUI 3
- no NuGet PackageReference by default
- no Office/Excel COM interop by default

## 3. Application anatomy

Every user-facing HS utility should normally contain:

1. Native Windows window chrome for reliable minimize/maximize/drag behavior.
2. HSSuite header with HS mark, application name, short purpose and version.
3. One clear primary workspace.
4. A small number of visually grouped cards rather than dense unstructured controls.
5. One obvious primary action.
6. Persistent bottom status bar for state, warnings and progress text.
7. Consistent success/warning/error language and colors.

Avoid unnecessary navigation for a single-purpose utility. Add tabs/pages only when they reduce complexity.

## 4. Data and output rules

- Default app-generated output root: `AppDomain.CurrentDomain.BaseDirectory`.
- Never silently overwrite an existing generated file.
- Use a unique suffix such as `_2`, `_3`, etc. when a name already exists.
- Do not write application data to Temp/AppData/LocalAppData by default.
- Source data mutation rules are product-specific and must be documented in that app's `AGENTS.md`/README.
- Prefer read-only inspection whenever mutation is not required for the app's purpose.

## 5. Interaction rules

- Primary action uses the suite accent button.
- Destructive actions, where a product genuinely needs them, must be visually distinct and require appropriate confirmation.
- Disable impossible actions rather than allowing predictable errors.
- Long operations must keep the UI responsive and expose progress/state.
- Cancellation should be supported for long operations when practical.
- Errors should be actionable and avoid raw stack traces in the normal UI.

## 6. Naming and copy

- App names: short `HS` + noun/verb concept.
- Buttons: verbs (`Scan starten`, `Exportieren`, `Vergleichen`).
- Status text: short present-state descriptions (`Bereit`, `Scan läuft…`, `Export abgeschlossen`).
- Avoid marketing language inside production tools.

## 7. Release consistency

Every app uses semantic version branches and the standard PR/CI/release workflow from `AGENTS.md`. Release assets should include the EXE, a ZIP and a SHA-256 checksum.

## 8. Exceptions

The template is a default, not a prison. If a product needs a different framework, storage location, dependency, window model or file mutation behavior, document the reason in that product repo rather than quietly diverging.
