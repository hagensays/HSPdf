# HSPdf

HSPdf is a deliberately lean, read-only PDF reader for Windows office PCs.

It is designed for the common case where a PDF only needs to open quickly, stay readable and respond immediately to keyboard input. It does not try to reproduce Acrobat's editing, cloud, annotation or account features.

## v0.1.0 scope

- native WPF shell on .NET Framework 4.7.2
- PDF rendering through the Windows 10 `Windows.Data.Pdf` API
- one-page viewer with a narrow side action bar
- open PDF from the file picker, drag/drop or command line
- previous/next page navigation
- mouse-wheel page navigation at page edges
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

## Deliberate v0.1.0 limitations

No text selection/search, annotations, form editing, password entry or printing. These are intentionally outside the first release so the reader stays small and fast. Password-protected or malformed PDFs fail with a short user-facing error instead of attempting recovery.
