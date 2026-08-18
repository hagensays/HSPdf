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

## HSPdf Product Rules

- HSPdf is a read-only viewer. Never modify, rename, delete, replace or overwrite a source PDF.
- PDFium is the explicitly approved PDF engine from v0.4.0 onward. Do not reintroduce `Windows.Data.Pdf` or add a second PDF engine without a concrete need.
- Build PDFium only from the official `pdfium.googlesource.com/pdfium.git` source at the exact commit pinned in `vendor/pdfium/PDFIUM_COMMIT.txt`. Do not substitute an arbitrary prebuilt DLL.
- The runtime architecture is x64 because the bundled native `pdfium.dll` is x64. HSPdf remains WPF on .NET Framework 4.7.2.
- The distributable runtime is `HSPdf.exe` + `pdfium.dll` + PDFium license/build notices. No installer, NuGet package, separately installed VC++ runtime or admin action may be required.
- Keep PDFium configured lean for HSPdf: V8/JavaScript off, XFA off, Skia off unless a future feature explicitly requires one of them.
- Do not add network access, telemetry, update checks, cloud features or account features. PDFium acquisition is build-time only; HSPdf must never download it at runtime.
- Keep PDF rendering bounded: maximum requested zoom is 400%, render pixel counts are capped and the bitmap cache remains small.
- Prefer one rendered page at a time. Do not eagerly rasterize an entire document for normal viewing.
- All PDFium API access must remain serialized because PDFium's embedder API is not thread-safe. The native bridge owns this serialization.
- Printing uses the Windows modern print UI (`Windows.Graphics.Printing`) through the native `pdfium.dll` bridge. PDFium supplies document/attachment access and printing-mode page rendering. Do not reintroduce the WPF `PrintDialog`/`DocumentPaginator`, launch Adobe or another registered PDF application, or create temporary print files.
- `Alle drucken` must open the Windows print UI before doing expensive PDF/attachment preparation. It uses natural filename order for top-level PDFs and places each embedded PDF attachment immediately after its parent PDF in the print sequence.
- The folder companion list may enumerate only PDFs in the opened PDF's directory and must not recurse into subfolders.
- Attachment discovery uses PDFium's embedded-file API and is limited to embedded files whose names end in `.pdf`.
- Embedded PDF attachments may be extracted only in memory for display metadata and printing; never persist them to disk automatically.
- Password entry, text extraction/search, annotations and editing remain outside the current UI scope unless explicitly requested later, even though PDFium can enable future work in those areas.

## Naming

- Product repositories and executables should normally use the `HS` prefix: `HSScanner`, `HSRenamer`, `HSCompare`, etc.
- Release branches: `vMAJOR.MINOR.PATCH`.
- Public release executable: `<AppName>-vMAJOR.MINOR.PATCH.exe`.
- PDFium runtime asset: `pdfium.dll` plus `PDFium-LICENSES.txt` and `PDFium-BUILD.txt`.
