param(
    [string]$Target = (Join-Path (Split-Path -Parent $PSScriptRoot) 'HSPdf.sln'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

Write-Host "Building HSPdf $Configuration / $Platform"
& msbuild $Target /m /t:Rebuild "/p:Configuration=$Configuration" "/p:Platform=$Platform"
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}
