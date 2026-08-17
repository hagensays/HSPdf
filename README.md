# HSPdf

HSPdf is a deliberately lean, read-only PDF reader for Windows office PCs.

It is designed for the common case where a PDF only needs to open quickly, stay readable and respond immediately to keyboard input. It does not try to reproduce Acrobat's editing, cloud, annotation or account features.

## v0.3.0 scope

- native WPF shell on .NET Framework 4.7.2
- PDF rendering through the Windows 10 `Windows.Data.Pdf` API
- center-only PDF viewport with equal, jointly resizable left/right side panels
- split HSSuite header and footer aligned to the side-panel edges
- left panel: open, page jump, navigation, zoom, fit, rotate, print, open folder, copy filename, copy full path
- right panel always shows the current PDF first, then a separator, then the other PDFs in the same folder
- best-effort read-only discovery of common embedded PDF attachments; attachment names are shown as indented `├─` / `└─` children below their parent PDF
- double-click the page to toggle Fit Width / Fit Height
- pan a zoomed page with middle-mouse drag or Space + left-drag
- tap Space without dragging to keep the existing next-page behavior
- `F11` toggles a PDF-only fullscreen view; `Esc` leaves fullscreen
- Fit Height and Fit Width reserve enough viewport space to avoid residual fit-mode scrollbars
- printing hands the original opened PDF file to the Windows-registered PDF print handler
- open PDF from the file picker, drag/drop or command line
- zoom from 10% to 400%
- clockwise 90-degree view rotation
- small in-memory cache for recently rendered pages
- no network access
- no writes to the source PDF

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Open PDF | `Ctrl+O` |
| Print original PDF | `Ctrl+P` |
| Previous page | `Page Up` or `Left` |
| Next page | `Page Down`, `Right` or tap `Space` |
| Jump to page | enter a page number in the left panel and press `Enter` |
| Zoom in | `+` |
| Zoom out | `-` |
| Fit height | `H` |
| Fit width | `W` |
| Toggle fit mode | double-click PDF |
| Pan | middle-mouse drag or `Space` + left-drag |
| Fullscreen | `F11` |
| Leave fullscreen | `F11` or `Esc` |
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

The right-side folder list only enumerates sibling `*.pdf` files in the opened document's directory. It does not recurse into subfolders. Attachment discovery is also read-only and bounded: HSPdf inspects limited PDF byte ranges for common `Filespec`/`EmbeddedFiles` metadata and only displays names. It does not extract attachments. PDFs that store attachment metadata only inside unsupported compressed/object-stream structures may show no attachments rather than falling back to a heavy PDF library.

Printing does not print HSPdf's rendered bitmap. HSPdf invokes Windows' registered `print` action for the original PDF path. The exact printer UI and behavior are therefore determined by the PDF print handler registered on that Windows installation; HSPdf cannot force that handler to use a particular modern or classic dialog.

## Deliberate limitations

No text selection/search, annotations, form editing or password entry. Password-protected or malformed PDFs fail with a short user-facing error instead of attempting recovery.
