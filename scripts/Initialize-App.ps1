param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^HS[A-Za-z0-9]+$')]
    [string]$AppName,

    [Parameter(Mandatory = $true)]
    [string]$Description
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$templateName = 'HSTemplate'

if ($AppName -eq $templateName) {
    throw 'Choose a real product name, not HSTemplate.'
}

$projectDir = Join-Path $root 'src\HSTemplate'
if (-not (Test-Path $projectDir)) {
    throw 'Template project folder src\HSTemplate was not found. This repo may already be initialized.'
}

$textExtensions = @('.md', '.sln', '.csproj', '.xaml', '.cs', '.ps1', '.yml', '.yaml')
$files = Get-ChildItem $root -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/]\.git[\\/]' -and $textExtensions -contains $_.Extension
}

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $updated = $content.Replace($templateName, $AppName).Replace('{{APP_DESCRIPTION}}', $Description)
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($file.FullName, $updated, [System.Text.UTF8Encoding]::new($false))
    }
}

$oldProject = Join-Path $root 'src\HSTemplate'
$newProject = Join-Path $root ("src\{0}" -f $AppName)
Move-Item -LiteralPath $oldProject -Destination $newProject

$oldSln = Join-Path $root 'HSTemplate.sln'
$newSln = Join-Path $root ("{0}.sln" -f $AppName)
if (Test-Path $oldSln) {
    Move-Item -LiteralPath $oldSln -Destination $newSln
}

$oldProjectFile = Join-Path $newProject 'HSTemplate.csproj'
$newProjectFile = Join-Path $newProject ("{0}.csproj" -f $AppName)
if (Test-Path $oldProjectFile) {
    Move-Item -LiteralPath $oldProjectFile -Destination $newProjectFile
}

Write-Host "Initialized HSSuite app: $AppName"
Write-Host "Description: $Description"
Write-Host 'Review the diff, update product-specific AGENTS.md rules, and commit only on a version branch.'
