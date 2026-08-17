param(
    [Parameter(Mandatory = $true)]
    [string]$PdfiumRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $PdfiumRoot).Path
$topLicense = Join-Path $root 'LICENSE'
if (-not (Test-Path $topLicense)) {
    throw "PDFium LICENSE missing: $topLicense"
}

$entries = New-Object System.Collections.Generic.List[object]
$entries.Add([pscustomobject]@{ Name = 'PDFium'; Path = $topLicense; Source = 'LICENSE' })
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$null = $seen.Add((Resolve-Path $topLicense).Path)

$readmes = Get-ChildItem -Path (Join-Path $root 'third_party') -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('README.pdfium', 'README.chromium') }

foreach ($readme in $readmes) {
    $text = Get-Content $readme.FullName -Raw
    if ($text -notmatch '(?mi)^Shipped:\s*yes\s*$') {
        continue
    }

    $nameMatch = [regex]::Match($text, '(?mi)^Name:\s*(.+?)\s*$')
    $licenseMatch = [regex]::Match($text, '(?mi)^License File:\s*(.+?)\s*$')
    if (-not $licenseMatch.Success) {
        throw "Shipped dependency has no License File field: $($readme.FullName)"
    }

    $dependencyName = if ($nameMatch.Success) { $nameMatch.Groups[1].Value.Trim() } else { $readme.Directory.Name }
    $licenseNames = $licenseMatch.Groups[1].Value -split '[,;]' |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and $_ -notmatch '^(NOT_SHIPPED|N/A|NONE)$' }

    foreach ($licenseName in $licenseNames) {
        $candidate = Join-Path $readme.Directory.FullName $licenseName
        if (-not (Test-Path $candidate)) {
            throw "Declared license file missing for $dependencyName`: $candidate"
        }

        $resolved = (Resolve-Path $candidate).Path
        if ($seen.Add($resolved)) {
            $relative = $resolved.Substring($root.Length).TrimStart('\', '/')
            $entries.Add([pscustomobject]@{ Name = $dependencyName; Path = $resolved; Source = $relative })
        }
    }
}

$builder = New-Object System.Text.StringBuilder
$null = $builder.AppendLine('HSPdf bundled PDFium license notices')
$null = $builder.AppendLine('Generated from the pinned PDFium source checkout.')
$null = $builder.AppendLine()

foreach ($entry in $entries) {
    $null = $builder.AppendLine(('=' * 78))
    $null = $builder.AppendLine($entry.Name)
    $null = $builder.AppendLine("Source: $($entry.Source)")
    $null = $builder.AppendLine(('=' * 78))
    $null = $builder.AppendLine((Get-Content $entry.Path -Raw).TrimEnd())
    $null = $builder.AppendLine()
    $null = $builder.AppendLine()
}

$parent = Split-Path -Parent $OutputPath
if ($parent -and -not (Test-Path $parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Collected $($entries.Count) PDFium/license notice files into $OutputPath"
