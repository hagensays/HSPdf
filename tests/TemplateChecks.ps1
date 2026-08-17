$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\HSTemplate'
$projectPath = Join-Path $sourceRoot 'HSTemplate.csproj'

if (-not (Test-Path $projectPath)) {
    throw 'HSTemplate project is missing.'
}

$project = Get-Content $projectPath -Raw
if ($project -notmatch '<TargetFrameworkVersion>v4\.7\.2</TargetFrameworkVersion>') {
    throw 'Template must target .NET Framework 4.7.2.'
}
if ($project -match '<PackageReference') {
    throw 'Template must not contain NuGet PackageReference dependencies.'
}

$sourceFiles = Get-ChildItem $sourceRoot -Recurse -File | Where-Object { $_.Extension -in '.cs', '.xaml' }
$forbiddenStorage = @(
    'Path.GetTempPath',
    'Environment.SpecialFolder.ApplicationData',
    'Environment.SpecialFolder.LocalApplicationData'
)
foreach ($needle in $forbiddenStorage) {
    $matches = $sourceFiles | Select-String -SimpleMatch $needle
    if ($matches) {
        throw "Template source uses forbidden default storage API '$needle'."
    }
}

$outputService = Get-Content (Join-Path $sourceRoot 'Infrastructure\OutputPathService.cs') -Raw
if ($outputService -notmatch 'AppDomain\.CurrentDomain\.BaseDirectory') {
    throw 'OutputPathService must root generated output beside the executable.'
}
if ($outputService -notmatch 'FileMode\.CreateNew') {
    throw 'OutputPathService must expose non-overwriting CreateNew output.'
}

foreach ($required in @('Themes\Colors.xaml', 'Themes\Controls.xaml', 'MainWindow.xaml')) {
    if (-not (Test-Path (Join-Path $sourceRoot $required))) {
        throw "Required suite UI file missing: $required"
    }
}

$releaseWorkflow = Get-Content (Join-Path $root '.github\workflows\release.yml') -Raw
$releaseRequirements = @(
    'git push origin "refs/tags/${tag}:refs/tags/${tag}"',
    '--verify-tag',
    'git ls-remote --tags origin "refs/tags/$tag"',
    'git push origin ":refs/heads/$branch"',
    'Release tag disappeared during branch cleanup'
)
foreach ($requiredFragment in $releaseRequirements) {
    if (-not $releaseWorkflow.Contains($requiredFragment)) {
        throw "Release workflow is missing required tag/cleanup invariant: $requiredFragment"
    }
}

if ($releaseWorkflow.Contains('refs/tags/$tag:refs/tags/$tag')) {
    throw 'PowerShell tag refspec must use ${tag} before a colon to avoid scoped-variable parsing.'
}

Write-Host 'HSSuite template invariants passed.'
