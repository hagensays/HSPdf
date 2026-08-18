# HSPdf

HSPdf is a deliberately lean, read-only PDF reader for Windows office PCs.

It is designed for the common case where a PDF only needs to open quickly, stay readable and respond immediately to keyboard input. From v0.4.0 onward the PDF engine is PDFium rather than `Windows.Data.Pdf`.

## v0.4.0 scope

- native x64 WPF shell on .NET Framework 4.7.2
- rendering, page geometry, embedded-file discovery and print rendering through a pinned PDFium build
- local `pdfium.dll`; no installer, admin rights, NuGet package or runtime download
- center-only PDF viewport with equal, jointly resizable left/right side panels
- split HSSuite header and footer aligned to the side-panel edges
- left panel: open, page jump, navigation, zoom, fit, rotate, print, open folder, copy filename, copy full path
- right panel always shows the current PDF first, then a separator, then the other PDFs in the same folder
- embedded **PDF** attachments are read through PDFium and shown as indented `├─` / `└─` children below their parent PDF
- double-click the page to toggle Fit Width / Fit Height
- pan a zoomed page with middle-mouse drag or Space + left-drag
- tap Space without dragging to keep the next-page behavior
- `F11` toggles a PDF-only fullscreen view; `Esc` leaves fullscreen
- Fit Height and Fit Width reserve enough viewport space to avoid residual fit-mode scrollbars
- printing uses the normal HSPdf/WPF Windows print dialog and PDFium's printing render mode; Adobe is not launched
- `Alle drucken` prints every PDF in the current folder in natural filename order and inserts each embedded PDF attachment immediately after its parent PDF
- embedded PDF attachments are extracted only in memory for printing; no temporary attachment files are created
- open PDF from the file picker, drag/drop or command line
- zoom from 10% to 400%
- clockwise 90-degree view rotation rendered by PDFium
- small in-memory cache for recently rendered pages
- no network access at runtime
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

## Compatibility and runtime files

Runtime target: 64-bit Windows 10 1809/LTSC 2019 or newer with .NET Framework 4.7.2 or newer.

Keep these files together:

```text
HSPdf.exe
pdfium.dll
PDFium-LICENSES.txt
PDFium-BUILD.txt
```

The GitHub release ZIP contains the complete runtime folder. If the EXE and DLL are downloaded separately, place them in the same directory.

PDFium is built by CI from the official source repository at the exact revision in `vendor/pdfium/PDFIUM_COMMIT.txt`. HSPdf's build disables V8/JavaScript, XFA and Skia and produces a complete x64 native PDFium runtime through the small bridge in `vendor/pdfium/bridge`.

Build HSPdf itself with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

Build the pinned PDFium runtime with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\BuildPdfium.ps1
```

## Safety model

HSPdf opens source PDFs read-only. It does not edit, rename, delete or overwrite source files and does not write application state to `%TEMP%`, `%APPDATA%` or `%LOCALAPPDATA%`.

The right-side folder list only enumerates sibling `*.pdf` files in the opened document's directory. It does not recurse into subfolders. PDFium's embedded-file API is used for attachment discovery; HSPdf surfaces only embedded files whose names end in `.pdf`. Attachment bytes are extracted only in memory when printing them.

Printing renders pages on demand through PDFium into the standard WPF/Windows print pipeline. The registered PDF application is not launched.

## Deliberate limitations

The v0.4.0 UI still deliberately omits text search/selection, annotations, form editing and password entry. PDFium gives HSPdf a much stronger engine for adding such features later, but the engine migration does not add UI complexity merely because the underlying API can support it.
