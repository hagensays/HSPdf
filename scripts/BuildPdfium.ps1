param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\pdfium-runtime')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pinPath = Join-Path $repoRoot 'vendor\pdfium\PDFIUM_COMMIT.txt'
if (-not (Test-Path $pinPath)) { throw 'PDFium pin file is missing.' }
$commit = (Get-Content $pinPath -Raw).Trim()
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Invalid PDFium commit pin: $commit" }

$artifactRoot = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$buildLog = Join-Path $artifactRoot 'pdfium-build.log'
if (Test-Path $buildLog) { Remove-Item -Force $buildLog }

$baseTemp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } elseif ($env:TEMP) { $env:TEMP } else { $repoRoot }
$workRoot = Join-Path $baseTemp ("hspdf-pdfium-" + $commit.Substring(0, 12))
$depotTools = Join-Path $workRoot 'depot_tools'
$pdfiumRoot = Join-Path $workRoot 'pdfium'
$outDir = Join-Path $pdfiumRoot 'out\HSPdf'

if (Test-Path $workRoot) { Remove-Item -Recurse -Force $workRoot }
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

Write-Host "Building PDFium $commit for HSPdf (x64)"
git clone --depth 1 https://chromium.googlesource.com/chromium/tools/depot_tools.git $depotTools
if ($LASTEXITCODE -ne 0) { throw 'Could not clone depot_tools.' }

$env:PATH = "$depotTools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = '0'
$env:GYP_MSVS_VERSION = '2022'

git clone --filter=blob:none --no-checkout https://pdfium.googlesource.com/pdfium.git $pdfiumRoot
if ($LASTEXITCODE -ne 0) { throw 'Could not clone PDFium.' }
git -C $pdfiumRoot fetch --depth 1 origin $commit
if ($LASTEXITCODE -ne 0) { throw "Could not fetch pinned PDFium commit $commit." }
git -C $pdfiumRoot checkout --detach $commit
if ($LASTEXITCODE -ne 0) { throw "Could not checkout pinned PDFium commit $commit." }

$gclient = @"
solutions = [
  {
    "name": "pdfium",
    "url": "https://pdfium.googlesource.com/pdfium.git",
    "deps_file": "DEPS",
    "managed": False,
    "custom_deps": {},
    "custom_vars": {
      "checkout_configuration": "minimal",
    },
    "safesync_url": "",
  },
]
target_os = []
"@
[System.IO.File]::WriteAllText((Join-Path $workRoot '.gclient'), $gclient, [System.Text.UTF8Encoding]::new($false))

Push-Location $workRoot
try {
    gclient sync --no-history --force --delete_unversioned_trees
    if ($LASTEXITCODE -ne 0) { throw 'gclient sync failed.' }
} finally {
    Pop-Location
}

$actualCommit = (git -C $pdfiumRoot rev-parse HEAD).Trim()
if ($actualCommit -ne $commit) {
    throw "PDFium checkout moved unexpectedly. Expected $commit, got $actualCommit."
}

$bridgeSource = Join-Path $repoRoot 'vendor\pdfium\bridge'
$bridgeTarget = Join-Path $pdfiumRoot 'hspdf_bridge'
Copy-Item -Recurse -Force $bridgeSource $bridgeTarget

# GN only emits targets reachable from the generated graph. Keep the checked-in
# bridge BUILD.gn authoritative and expose it through one small root group.
$rootBuildPath = Join-Path $pdfiumRoot 'BUILD.gn'
$bridgeTargetText = @"

# HSPdf reproducible embedder bridge. Injected by scripts/BuildPdfium.ps1.
group("hspdf_pdfium") {
  deps = [ "//hspdf_bridge:hspdf_pdfium" ]
}
"@
Add-Content -Path $rootBuildPath -Value $bridgeTargetText -Encoding utf8

$gnArgs = @"
is_debug = false
target_cpu = "x64"
symbol_level = 0
use_remoteexec = false
is_component_build = false
pdf_is_complete_lib = true
pdf_is_standalone = false
pdf_enable_v8 = false
pdf_enable_xfa = false
pdf_use_skia = false
pdf_enable_fontations = false
clang_use_chrome_plugins = false
"@
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
[System.IO.File]::WriteAllText((Join-Path $outDir 'args.gn'), $gnArgs, [System.Text.UTF8Encoding]::new($false))

Push-Location $pdfiumRoot
try {
    gn gen out/HSPdf
    if ($LASTEXITCODE -ne 0) { throw 'PDFium GN generation failed.' }

    & autoninja -C out/HSPdf hspdf_pdfium 2>&1 | Tee-Object -FilePath $buildLog
    $nativeExitCode = $LASTEXITCODE
    if ($nativeExitCode -ne 0) {
        $tail = @(Get-Content -Path $buildLog -Tail 80)
        Write-Host ''
        Write-Host '========== PDFium native build failure tail =========='
        $tail | ForEach-Object { Write-Host $_ }
        Write-Host '======================================================='

        if ($env:GITHUB_ACTIONS -eq 'true') {
            $annotation = ($tail -join "`n")
            $annotation = $annotation.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
            Write-Host "::error title=PDFium native build failed::$annotation"
        }
        throw "PDFium native build failed with exit code $nativeExitCode. Full log: $buildLog"
    }
} finally {
    Pop-Location
}

$dll = Get-ChildItem -Path $outDir -Filter pdfium.dll -File -Recurse | Select-Object -First 1
if (-not $dll) { throw 'Built pdfium.dll was not found.' }
if ($dll.Length -lt 1MB) { throw "Built pdfium.dll is unexpectedly small: $($dll.Length) bytes" }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $vsRoot = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if ($vsRoot) {
        $dumpbin = Get-ChildItem -Path (Join-Path $vsRoot 'VC\Tools\MSVC') -Filter dumpbin.exe -File -Recurse |
            Where-Object { $_.FullName -match 'Hostx64[\\/]x64' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($dumpbin) {
            $imports = (& $dumpbin.FullName /dependents $dll.FullName | Out-String)
            if ($imports -match '(?im)\b(?:VCRUNTIME|MSVCP)\d*[^\s]*\.dll\b') {
                throw "pdfium.dll depends on a VC++ redistributable:`n$imports"
            }
            Write-Host 'Verified: pdfium.dll has no VCRUNTIME/MSVCP runtime dependency.'
        }
    }
}

$runtime = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $runtime) { Remove-Item -Recurse -Force $runtime }
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Copy-Item $dll.FullName (Join-Path $runtime 'pdfium.dll')

& (Join-Path $PSScriptRoot 'CollectPdfiumLicenses.ps1') -PdfiumRoot $pdfiumRoot -OutputPath (Join-Path $runtime 'PDFium-LICENSES.txt')

$buildInfo = @"
HSPdf PDFium runtime
PDFium commit: $commit
Source: https://pdfium.googlesource.com/pdfium.git
Architecture: x64
Configuration: Release
JavaScript/V8: disabled
XFA: disabled
Skia: disabled
PDFium complete static library: enabled
HSPdf bridge output: pdfium.dll

GN args:
$gnArgs
"@
[System.IO.File]::WriteAllText((Join-Path $runtime 'PDFium-BUILD.txt'), $buildInfo, [System.Text.UTF8Encoding]::new($false))

Write-Host "PDFium runtime ready: $runtime"
Get-ChildItem $runtime | Format-Table Name, Length
