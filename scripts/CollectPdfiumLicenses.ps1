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
        # Chromium/PDFium README metadata uses //path/to/file to mean a path
        # relative to the checkout root, not relative to the README directory.
        # Resolve that syntax explicitly so declarations such as
        # //third_party/zlib/LICENSE point at the actual shared license file.
        $rootRelative = $licenseName -match '^[\\/]{2}'
        if ($rootRelative) {
            $relativeLicense = $licenseName -replace '^[\\/]+', ''
            $candidate = Join-Path $root $relativeLicense
        } else {
            $candidate = Join-Path $readme.Directory.FullName $licenseName
        }

        if (-not (Test-Path $candidate)) {
            # README.chromium describes Chromium's dependency in general. With a
            # minimal PDFium checkout, conditionally disabled dependencies (for
            # example V8-only dragonbox) can leave an absent *or empty* checkout
            # root behind. Skip only that precise case. If source content exists
            # but the declared license does not, fail closed so we never ship
            # incomplete notices for a dependency that is actually present.
            $pathParts = $licenseName -split '[\\/]'
            if (-not $rootRelative -and $pathParts.Count -gt 1) {
                $declaredSourceRoot = Join-Path $readme.Directory.FullName $pathParts[0]
                $sourceRootMissing = -not (Test-Path $declaredSourceRoot)
                $sourceRootEmpty = $false
                if (-not $sourceRootMissing) {
                    $sourceRootEmpty = -not [bool](Get-ChildItem -Path $declaredSourceRoot -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
                }

                if ($sourceRootMissing -or $sourceRootEmpty) {
                    Write-Host "Skipping license metadata for disabled dependency $dependencyName (checkout '$($pathParts[0])' is absent or empty)."
                    continue
                }
            }

            throw "Declared license file missing for $dependencyName`: $candidate"
        }

        $resolved = (Resolve-Path $candidate).Path
        if ($seen.Add($resolved)) {
            $relative = $resolved.Substring($root.Length) -replace '^[\\/]+', ''
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
