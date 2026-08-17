param(
    [string]$Target = (Join-Path (Split-Path -Parent $PSScriptRoot) 'HSPdf.sln'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Platform = 'Any CPU'
)

$ErrorActionPreference = 'Stop'

$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$metadataRoot = Join-Path $programFilesX86 'Windows Kits\10\UnionMetadata'
if (-not (Test-Path $metadataRoot)) {
    throw "Windows 10 SDK UnionMetadata was not found: $metadataRoot"
}

$candidates = Get-ChildItem -Path $metadataRoot -Filter Windows.winmd -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]Facade[\\/]' }
if (-not $candidates) {
    throw 'No full Windows.winmd was found. Install a Windows 10 SDK build environment.'
}

$winmd = $candidates |
    Sort-Object @{ Expression = { try { [version]$_.Directory.Name } catch { [version]'0.0' } }; Descending = $true } |
    Select-Object -First 1

Write-Host "Using Windows metadata: $($winmd.FullName)"
& msbuild $Target /m /t:Rebuild "/p:Configuration=$Configuration" "/p:Platform=$Platform" "/p:WindowsWinMdPath=$($winmd.FullName)"
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}
