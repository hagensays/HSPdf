# HSPdf

HSPdf is a deliberately lean, read-only PDF reader for Windows office PCs.

It is designed for the common case where a PDF only needs to open quickly, stay readable and respond immediately to keyboard input. It does not try to reproduce Acrobat's editing, cloud, annotation or account features.

## v0.2.0 scope

- native WPF shell on .NET Framework 4.7.2
- PDF rendering through the Windows 10 `Windows.Data.Pdf` API
- center-only PDF viewport with equal left/right side panels
- both side panels resize together by dragging either divider
- split HSSuite header and footer: no window-wide bars across the PDF
- left action panel for open, navigation, zoom, fit, rotate and print
- right panel lists the other PDFs in the currently opened PDF's folder
- click another folder PDF to open it
- standard Windows print dialog via `Ctrl+P`
- open PDF from the file picker, drag/drop or command line
- previous/next page navigation
- Fit Height uses direct page-by-page mouse-wheel navigation
- Fit Width/manual zoom scroll inside the page and only change pages at the real top/bottom edge
- zoom from 10% to 400%
- fit height and fit width modes
- clockwise 90-degree view rotation
- small in-memory cache for recently rendered pages
- no network access
- no writes to the source PDF

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Open PDF | `Ctrl+O` |
| Print | `Ctrl+P` |
| Previous page | `Page Up` or `Left` |
| Next page | `Page Down` or `Right` |
| Zoom in | `+` |
| Zoom out | `-` |
| Fit height | `H` |
| Fit width | `W` |
| Rotate clockwise | `R` |
| First page | `Home` |
| Last page | `End` |
| Zoom with mouse | `Ctrl+Mouse Wheel` |

## Compatibility and dependencies

Runtime target: Windows 10 1809/LTSC 2019 or newer with .NET Framework 4.7.2 or newer.

HSPdf does not ship a third-party PDF engine. The executable calls the PDF renderer built into Windows through `Windows.Data.Pdf`. The Windows SDK metadata is required only when building the source; release users receive a single EXE.

Build with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

## Safety model

HSPdf opens source PDFs with read access only. It does not edit, rename, delete or overwrite source files and does not write application state to `%TEMP%`, `%APPDATA%` or `%LOCALAPPDATA%`.

The right-side folder list only enumerates sibling `*.pdf` files in the opened document's directory. It does not recurse into subfolders. Printing rasterizes pages in memory and sends them to the selected Windows printer; it does not create intermediate files.

## Deliberate limitations

No text selection/search, annotations, form editing or password entry. Password-protected or malformed PDFs fail with a short user-facing error instead of attempting recovery.
