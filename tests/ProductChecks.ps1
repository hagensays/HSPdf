$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\HSPdf'
$projectPath = Join-Path $sourceRoot 'HSPdf.csproj'

if (-not (Test-Path $projectPath)) {
    throw 'HSPdf project is missing.'
}

$project = Get-Content $projectPath -Raw
if ($project -notmatch '<TargetFrameworkVersion>v4\.7\.2</TargetFrameworkVersion>') {
    throw 'HSPdf must target .NET Framework 4.7.2.'
}
if ($project -match '<PackageReference') {
    throw 'HSPdf must not contain NuGet PackageReference dependencies.'
}
foreach ($requiredReference in @('System.Runtime.WindowsRuntime', 'WindowsWinMdPath', '<Reference Include="Windows"', '<Reference Include="ReachFramework"')) {
    if (-not $project.Contains($requiredReference)) {
        throw "HSPdf project is missing required reader/legacy-print build reference '$requiredReference'."
    }
}
foreach ($compileFile in @('MainWindow.Printing.cs', 'MainWindow.Features.cs', 'PdfAttachmentScanner.cs')) {
    if (-not $project.Contains("<Compile Include=\"$compileFile\"")) {
        throw "HSPdf project must compile $compileFile."
    }
}

$sourceFiles = Get-ChildItem $sourceRoot -Recurse -File | Where-Object { $_.Extension -in '.cs', '.xaml' }
foreach ($needle in @('Path.GetTempPath', 'Environment.SpecialFolder.ApplicationData', 'Environment.SpecialFolder.LocalApplicationData')) {
    if ($sourceFiles | Select-String -SimpleMatch $needle) {
        throw "HSPdf source uses forbidden default storage API '$needle'."
    }
}
foreach ($needle in @('File.Delete(', 'File.Move(', 'File.WriteAll', 'FileAccess.Write', 'FileMode.Create')) {
    if ($sourceFiles | Select-String -SimpleMatch $needle) {
        throw "HSPdf source violates the read-only PDF safety model with '$needle'."
    }
}

foreach ($required in @('Themes\Colors.xaml', 'Themes\Controls.xaml', 'MainWindow.xaml', 'MainWindow.Printing.cs', 'MainWindow.Features.cs', 'PdfAttachmentScanner.cs')) {
    if (-not (Test-Path (Join-Path $sourceRoot $required))) {
        throw "Required HSPdf file missing: $required"
    }
}

$readerSource = Get-Content (Join-Path $sourceRoot 'MainWindow.xaml.cs') -Raw
foreach ($fragment in @(
    'Windows.Data.Pdf',
    'FileAccess.Read',
    'PdfPageRenderOptions',
    'MaximumZoom = 4.0',
    'Directory.EnumerateFiles',
    'SearchOption.TopDirectoryOnly',
    '_viewMode == ViewMode.FitHeight'
)) {
    if (-not $readerSource.Contains($fragment)) {
        throw "HSPdf reader invariant missing: $fragment"
    }
}

$printingSource = Get-Content (Join-Path $sourceRoot 'MainWindow.Printing.cs') -Raw
foreach ($fragment in @(
    'Verb = "print"',
    'UseShellExecute = true',
    'FileName = _documentPath',
    'PrintOriginalButton_Click'
)) {
    if (-not $printingSource.Contains($fragment)) {
        throw "HSPdf original-PDF print invariant missing: $fragment"
    }
}
if ($printingSource.Contains('PdfPageRenderOptions') -or $printingSource.Contains('PrintDialog')) {
    throw 'The active original-PDF print route must not rasterize the PDF or use the WPF PrintDialog.'
}

$featureSource = Get-Content (Join-Path $sourceRoot 'MainWindow.Features.cs') -Raw
foreach ($fragment in @(
    'CurrentPageTextBox_KeyDown',
    'OpenFolderButton_Click',
    'CopyNameButton_Click',
    'CopyPathButton_Click',
    'PdfCanvas_PreviewMouseLeftButtonDown',
    'PdfScrollViewer_PreviewMouseDownV030',
    'Cursors.ScrollAll',
    'Window_PreviewKeyDownV030',
    'Key.F11',
    'ToggleFullScreen',
    'PdfAttachmentScanner.Scan(_documentPath)'
)) {
    if (-not $featureSource.Contains($fragment)) {
        throw "HSPdf v0.3.0 feature invariant missing: $fragment"
    }
}

$attachmentSource = Get-Content (Join-Path $sourceRoot 'PdfAttachmentScanner.cs') -Raw
foreach ($fragment in @(
    'FileAccess.Read',
    'SegmentBytes = 2 * 1024 * 1024',
    'MaxAttachments = 64',
    '"/Filespec"',
    '"/EF"',
    '"├─ "',
    '"└─ "',
    'PdfAttachmentTreeConverter'
)) {
    if (-not $attachmentSource.Contains($fragment)) {
        throw "HSPdf attachment scanner invariant missing: $fragment"
    }
}

$xaml = Get-Content (Join-Path $sourceRoot 'MainWindow.xaml') -Raw
foreach ($fragment in @(
    'LeftSidebarColumn',
    'RightSidebarColumn',
    'LeftSplitterColumn',
    'RightSplitterColumn',
    'LeftResizeHandle',
    'RightResizeHandle',
    'FolderPdfListBox',
    'CurrentPdfNameTextBlock',
    'CurrentPdfAttachmentTextBlock',
    'PdfAttachmentTreeConverter',
    'CurrentPageTextBox',
    'OpenFolderButton',
    'CopyNameButton',
    'CopyPathButton',
    'PrintButton',
    'PreviewKeyDown="Window_PreviewKeyDownV030"',
    'PreviewKeyUp="Window_PreviewKeyUpV030"',
    'Click="PrintOriginalButton_Click"',
    'PreviewMouseLeftButtonDown="PdfCanvas_PreviewMouseLeftButtonDown"',
    'x:Name="PdfCanvas" Margin="16"',
    'BorderThickness="0,0,1,0"',
    'BorderThickness="1,0,0,0"'
)) {
    if (-not $xaml.Contains($fragment)) {
        throw "HSPdf v0.3.0 layout/route invariant missing: $fragment"
    }
}

$buildScript = Get-Content (Join-Path $root 'scripts\Build.ps1') -Raw
if (-not $buildScript.Contains('WindowsWinMdPath') -or -not $buildScript.Contains('Facade')) {
    throw 'Build.ps1 must resolve full Windows SDK metadata and pass WindowsWinMdPath.'
}

$releaseWorkflow = Get-Content (Join-Path $root '.github\workflows\release.yml') -Raw
$releaseRequirements = @(
    "'Product invariants'",
    'git push origin "refs/tags/${tag}:refs/tags/${tag}"',
    '--verify-tag',
    'git ls-remote --tags origin "refs/tags/$tag"',
    'git push origin ":refs/heads/$branch"',
    'Release tag disappeared during branch cleanup'
)
foreach ($requiredFragment in $releaseRequirements) {
    if (-not $releaseWorkflow.Contains($requiredFragment)) {
        throw "Release workflow is missing required invariant: $requiredFragment"
    }
}

if ($releaseWorkflow.Contains('refs/tags/$tag:refs/tags/$tag')) {
    throw 'PowerShell tag refspec must use ${tag} before a colon to avoid scoped-variable parsing.'
}

if (Test-Path (Join-Path $root 'HSTemplate.sln')) {
    throw 'Template solution name remains after HSPdf initialization.'
}

Write-Host 'HSPdf product invariants passed.'
