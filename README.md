# HSPdf

HSPdf is a deliberately lean, read-only PDF reader for Windows office PCs.

It is designed for the common case where a PDF only needs to open quickly, stay readable and respond immediately to keyboard input. It does not try to reproduce Acrobat's editing, cloud, annotation or account features.

## v0.3.1 scope

- native WPF shell on .NET Framework 4.7.2
- PDF rendering through the Windows 10 `Windows.Data.Pdf` API
- center-only PDF viewport with equal, jointly resizable left/right side panels
- split HSSuite header and footer aligned to the side-panel edges
- left panel: open, page jump, navigation, zoom, fit, rotate, print, open folder, copy filename, copy full path
- right panel always shows the current PDF first, then a separator, then the other PDFs in the same folder
- embedded **PDF** attachments are shown as indented `├─` / `└─` children below their parent PDF
- attachment metadata in common Flate-compressed PDF object streams is supported without adding a third-party PDF library
- double-click the page to toggle Fit Width / Fit Height
- pan a zoomed page with middle-mouse drag or Space + left-drag
- tap Space without dragging to keep the existing next-page behavior
- `F11` toggles a PDF-only fullscreen view; `Esc` leaves fullscreen
- Fit Height and Fit Width reserve enough viewport space to avoid residual fit-mode scrollbars
- printing uses HSPdf's own Windows/WPF print dialog and does not launch Adobe or another registered PDF application
- `Alle drucken` prints every PDF in the current folder in natural filename order and inserts each embedded PDF attachment immediately after its parent PDF
- embedded PDF attachments are decoded only in memory for printing; no temporary attachment files are created
- open PDF from the file picker, drag/drop or command line
- zoom from 10% to 400%
- clockwise 90-degree view rotation
- small in-memory cache for recently rendered pages
- no network access
- no writes to the source PDF

## Print order

For a folder containing `pdf1.pdf`, `pdf2.pdf`, `pdf3.pdf`, `pdf4.pdf`, where `pdf2.pdf` has one PDF attachment and `pdf3.pdf` has two, `Alle drucken` submits one ordered print document as:

```text
pdf1.pdf
pdf2.pdf
  └─ attachment.pdf
pdf3.pdf
  ├─ attachment1.pdf
  └─ attachment2.pdf
pdf4.pdf
```

Top-level filenames and attachment filenames use natural numeric ordering.

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Open PDF | `Ctrl+O` |
| Print current PDF | `Ctrl+P` |
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

The right-side folder list only enumerates sibling `*.pdf` files in the opened document's directory. It does not recurse into subfolders. Attachment discovery is also read-only and bounded. Only attachments whose filename ends in `.pdf` are surfaced. Common `Filespec` metadata in ordinary PDF objects and Flate-compressed object streams is decoded in memory. Embedded attachment bytes are only decoded when needed for printing and are never automatically written to disk.

Printing renders pages on demand through `Windows.Data.Pdf` into the standard WPF/Windows print pipeline. The registered PDF application is not launched.

## Deliberate limitations

No text selection/search, annotations, form editing or password entry. Unsupported attachment filter layouts fail closed rather than introducing a heavyweight PDF dependency. Password-protected or malformed PDFs fail with a short user-facing error instead of attempting recovery.
