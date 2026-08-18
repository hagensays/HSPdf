$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\HSPdf'
$projectPath = Join-Path $sourceRoot 'HSPdf.csproj'

if (-not (Test-Path $projectPath)) { throw 'HSPdf project is missing.' }
$project = Get-Content $projectPath -Raw
if ($project -notmatch '<TargetFrameworkVersion>v4\.7\.2</TargetFrameworkVersion>') { throw 'HSPdf must target .NET Framework 4.7.2.' }
if ($project -notmatch '<PlatformTarget>x64</PlatformTarget>') { throw 'PDFium HSPdf runtime must target x64.' }
if ($project -match '<PackageReference') { throw 'HSPdf must not contain NuGet PackageReference dependencies.' }
if ($project.Contains('<Reference Include="ReachFramework"')) { throw 'Legacy WPF paginator dependency ReachFramework must not return.' }
foreach ($forbidden in @('System.Runtime.WindowsRuntime', 'WindowsWinMdPath', '<Reference Include="Windows"')) {
    if ($project.Contains($forbidden)) { throw "Legacy Windows.Data.Pdf build dependency remains: $forbidden" }
}
foreach ($compileFile in @(
    'MainWindow.Printing.cs', 'MainWindow.Features.cs',
    'Infrastructure\NaturalStringComparer.cs',
    'Pdfium\PdfiumNative.cs', 'Pdfium\PdfiumDocument.cs',
    'Pdfium\PdfiumAttachmentDisplay.cs', 'Pdfium\PdfiumPrintSession.cs')) {
    if (-not $project.Contains('<Compile Include="' + $compileFile + '"')) { throw "HSPdf project must compile $compileFile." }
}
if (Test-Path (Join-Path $sourceRoot 'PdfAttachmentScanner.cs')) { throw 'Obsolete PdfAttachmentScanner.cs must remain removed.' }

$sourceFiles = Get-ChildItem $sourceRoot -Recurse -File | Where-Object { $_.Extension -in '.cs', '.xaml' }
foreach ($needle in @('Path.GetTempPath', 'Environment.SpecialFolder.ApplicationData', 'Environment.SpecialFolder.LocalApplicationData')) {
    if ($sourceFiles | Select-String -SimpleMatch $needle) { throw "HSPdf runtime source uses forbidden storage API '$needle'." }
}
foreach ($needle in @('File.Delete(', 'File.Move(', 'File.WriteAll', 'FileAccess.Write', 'FileMode.Create')) {
    if ($sourceFiles | Select-String -SimpleMatch $needle) { throw "HSPdf source violates the read-only PDF safety model with '$needle'." }
}
foreach ($legacy in @('Windows.Data.Pdf', 'Windows.Storage.Streams', 'PdfPageRenderOptions')) {
    if ($sourceFiles | Select-String -SimpleMatch $legacy) { throw "Legacy Windows PDF engine remains in runtime source: $legacy" }
}

foreach ($required in @(
    'Themes\Colors.xaml', 'Themes\Controls.xaml', 'MainWindow.xaml',
    'MainWindow.Printing.cs', 'MainWindow.Features.cs',
    'Infrastructure\NaturalStringComparer.cs',
    'Pdfium\PdfiumNative.cs', 'Pdfium\PdfiumDocument.cs',
    'Pdfium\PdfiumAttachmentDisplay.cs', 'Pdfium\PdfiumPrintSession.cs')) {
    if (-not (Test-Path (Join-Path $sourceRoot $required))) { throw "Required HSPdf file missing: $required" }
}

$readerSource = Get-Content (Join-Path $sourceRoot 'MainWindow.xaml.cs') -Raw
foreach ($fragment in @(
    'PdfiumDocument _document', 'PdfiumDocument.Open(path)', 'GetPageSizeDip', 'RenderPageAsync',
    'MaximumZoom = 4.0', 'MaxRenderPixels', 'MaxCachedPages = 3', 'Directory.EnumerateFiles',
    'SearchOption.TopDirectoryOnly', 'NaturalStringComparer.Instance', '_viewMode == ViewMode.FitHeight')) {
    if (-not $readerSource.Contains($fragment)) { throw "PDFium reader invariant missing: $fragment" }
}

$nativeSource = Get-Content (Join-Path $sourceRoot 'Pdfium\PdfiumNative.cs') -Raw
foreach ($fragment in @('DllImport(DllName', 'pdfium.dll', 'HSPDF_RenderPage', 'HSPDF_GetAttachmentCount', 'HSPDF_CopyAttachmentData', 'IntPtr.Size != 8')) {
    if (-not $nativeSource.Contains($fragment)) { throw "PDFium interop invariant missing: $fragment" }
}

$documentSource = Get-Content (Join-Path $sourceRoot 'Pdfium\PdfiumDocument.cs') -Raw
foreach ($fragment in @('PointsToDip = 96.0 / 72.0', 'RenderPageAsync', 'PixelFormats.Bgra32', 'GetPdfAttachments', 'MaxAttachmentBytes', 'EndsWith(".pdf"', 'LooksLikePdf')) {
    if (-not $documentSource.Contains($fragment)) { throw "PDFium document invariant missing: $fragment" }
}

$attachmentDisplay = Get-Content (Join-Path $sourceRoot 'Pdfium\PdfiumAttachmentDisplay.cs') -Raw
foreach ($fragment in @('PdfiumDocument.Open(path)', 'GetPdfAttachments(false)', 'NaturalStringComparer.Instance', 'PdfAttachmentTreeConverter', '"├─ "', '"└─ "')) {
    if (-not $attachmentDisplay.Contains($fragment)) { throw "PDFium attachment display invariant missing: $fragment" }
}
foreach ($legacy in @('Regex.Matches', 'DeflateStream', '"/ObjStm"', 'DecodeStream')) {
    if ($attachmentDisplay.Contains($legacy)) { throw "Old custom PDF parser remains: $legacy" }
}

$featureSource = Get-Content (Join-Path $sourceRoot 'MainWindow.Features.cs') -Raw
if (-not $featureSource.Contains('_document.GetPdfAttachments(false)')) { throw 'Current-document attachment display must use the already-open PDFium document.' }
if ($featureSource.Contains('PdfAttachmentScanner')) { throw 'Feature UI must not call the removed attachment scanner.' }

$printingSource = Get-Content (Join-Path $sourceRoot 'MainWindow.Printing.cs') -Raw
foreach ($fragment in @('PrintAllButton_Click', 'PdfiumPrintSession', 'NaturalStringComparer.Instance', 'WindowInteropHelper', 'StartModernPrintAsync')) {
    if (-not $printingSource.Contains($fragment)) { throw "Modern PDFium printing invariant missing: $fragment" }
}
foreach ($legacy in @('System.Windows.Controls.PrintDialog', 'PdfSequencePaginator', 'DocumentPaginator', 'dialog.PrintDocument', 'GetPdfAttachments(true)', 'Verb = "print"')) {
    if ($printingSource.Contains($legacy)) { throw "Legacy/eager printing path remains: $legacy" }
}

$printInterop = Get-Content (Join-Path $sourceRoot 'Pdfium\PdfiumPrintSession.cs') -Raw
foreach ($fragment in @('HSPDF_CreatePrintSession', 'HSPDF_PrintSessionAddFile', 'HSPDF_BeginModernPrint', 'HSPDF_GetModernPrintState', 'HSPDF_DestroyPrintSession')) {
    if (-not $printInterop.Contains($fragment)) { throw "Managed modern-print interop invariant missing: $fragment" }
}

$bridge = Get-Content (Join-Path $root 'vendor\pdfium\bridge\hspdf_bridge.cpp') -Raw
foreach ($fragment in @('SRWLOCK g_pdfium_lock = SRWLOCK_INIT', 'AcquireSRWLockExclusive', 'ReleaseSRWLockExclusive', 'FPDF_LoadCustomDocument', 'FPDF_LoadMemDocument64', 'FPDF_GetPageSizeByIndexF', 'FPDF_RenderPageBitmap', 'FPDF_PRINTING', 'FPDFDoc_GetAttachmentCount', 'FPDFAttachment_GetFile', 'FILE_SHARE_DELETE')) {
    if (-not $bridge.Contains($fragment)) { throw "Native PDFium bridge invariant missing: $fragment" }
}
if ($bridge.Contains('std::recursive_mutex') -or $bridge.Contains('catch (...)')) { throw 'Native PDFium bridge must remain compatible with Chromium no-exception/no-exit-destructor build flags.' }

$printBridgePath = Join-Path $root 'vendor\pdfium\bridge\hspdf_print.cpp'
if (-not (Test-Path $printBridgePath)) { throw 'Native modern print bridge is missing.' }
$printBridge = Get-Content $printBridgePath -Raw
foreach ($fragment in @('IPrintManagerInterop', 'ShowPrintUIForWindowAsync', 'IPrintDocumentPageSource', 'IPrintPreviewPageCollection', 'IPrintPreviewDxgiPackageTarget', 'CreatePrintControl', 'HSPDF_OpenDocumentMemory', 'HSPDF_GetAttachmentCount', 'HSPDF_RenderPage', 'printing_mode ? 1 : 0')) {
    if (-not $printBridge.Contains($fragment)) { throw "Native modern print invariant missing: $fragment" }
}
foreach ($forbidden in @('CreateProcess', 'ShellExecute', 'Path.GetTempPath')) {
    if ($printBridge.Contains($forbidden)) { throw "Native modern print bridge must not leave HSPdf or create print files: $forbidden" }
}

$bridgeBuild = Get-Content (Join-Path $root 'vendor\pdfium\bridge\BUILD.gn') -Raw
foreach ($fragment in @('hspdf_bridge.cpp', 'hspdf_print.cpp', 'd2d1.lib', 'd3d11.lib', 'runtimeobject.lib', 'windowscodecs.lib', 'output_name = "pdfium"')) {
    if (-not $bridgeBuild.Contains($fragment)) { throw "PDFium bridge build invariant missing: $fragment" }
}

$pin = (Get-Content (Join-Path $root 'vendor\pdfium\PDFIUM_COMMIT.txt') -Raw).Trim()
if ($pin -notmatch '^[0-9a-f]{40}$') { throw 'PDFium must be pinned to an exact 40-character commit.' }

$pdfiumBuild = Get-Content (Join-Path $root 'scripts\BuildPdfium.ps1') -Raw
foreach ($fragment in @('https://pdfium.googlesource.com/pdfium.git', 'checkout_configuration', 'minimal', 'target_cpu = "x64"', 'pdf_is_complete_lib = true', 'pdf_enable_v8 = false', 'pdf_enable_xfa = false', 'pdf_use_skia = false', '//hspdf_bridge:hspdf_pdfium', 'autoninja -C out/HSPdf hspdf_pdfium', 'CollectPdfiumLicenses.ps1', 'VCRUNTIME|MSVCP')) {
    if (-not $pdfiumBuild.Contains($fragment)) { throw "PDFium build invariant missing: $fragment" }
}

$buildScript = Get-Content (Join-Path $root 'scripts\Build.ps1') -Raw
if ($buildScript.Contains('WindowsWinMdPath') -or -not $buildScript.Contains("Platform = 'x64'")) { throw 'Build.ps1 must be a plain x64 .NET Framework build without Windows WinMD.' }

$xaml = Get-Content (Join-Path $sourceRoot 'MainWindow.xaml') -Raw
foreach ($fragment in @('CurrentPageTextBox', 'OpenFolderButton', 'CopyNameButton', 'CopyPathButton', 'PrintButton', 'PrintAllButton', 'CurrentPdfAttachmentTextBlock', 'PdfAttachmentTreeConverter', 'PreviewKeyDown="Window_PreviewKeyDownV030"')) {
    if (-not $xaml.Contains($fragment)) { throw "HSPdf UI invariant missing: $fragment" }
}

$ci = Get-Content (Join-Path $root '.github\workflows\ci.yml') -Raw
if (-not $ci.Contains('Build PDFium x64') -or -not $ci.Contains('BuildPdfium.ps1')) { throw 'CI must build and smoke-test the pinned PDFium x64 runtime.' }

$releaseWorkflow = Get-Content (Join-Path $root '.github\workflows\release.yml') -Raw
foreach ($fragment in @("'Build PDFium x64'", 'BuildPdfium.ps1', 'pdfium.dll', 'PDFium-LICENSES.txt', 'PDFIUM-BUILD.txt', 'pdfium.dll.sha256', 'git push origin ":refs/heads/$branch"')) {
    if (-not $releaseWorkflow.Contains($fragment)) { throw "Release workflow PDFium invariant missing: $fragment" }
}

Write-Host 'HSPdf PDFium v0.4.1 product invariants passed.'
