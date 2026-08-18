param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeDirectory
)

$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path $RuntimeDirectory).Path
$dll = Join-Path $runtime 'pdfium.dll'
if (-not (Test-Path $dll)) { throw "pdfium.dll missing: $dll" }

$nativeSource = @'
using System;
using System.Runtime.InteropServices;

public static class HSPdfPdfiumSmokeNative
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetDllDirectory(string path);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int HSPDF_Initialize();

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void HSPDF_Shutdown();

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr HSPDF_OpenDocumentMemory(IntPtr data, ulong length);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void HSPDF_CloseDocument(IntPtr document);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int HSPDF_GetPageCount(IntPtr document);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int HSPDF_GetPageSize(IntPtr document, int pageIndex, out double width, out double height);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int HSPDF_RenderPage(IntPtr document, int pageIndex, int width, int height, int rotation, int printing, IntPtr buffer, int stride);

    [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int HSPDF_GetAttachmentCount(IntPtr document);
}
'@
Add-Type -TypeDefinition $nativeSource -Language CSharp
if (-not [HSPdfPdfiumSmokeNative]::SetDllDirectory($runtime)) {
    throw "SetDllDirectory failed for $runtime"
}

$encoding = [System.Text.Encoding]::ASCII
$builder = New-Object System.Text.StringBuilder
$offsets = @{}
$null = $builder.Append("%PDF-1.4`n")
function Add-PdfObject([int]$number, [string]$body) {
    $offsets[$number] = $encoding.GetByteCount($builder.ToString())
    $null = $builder.Append("$number 0 obj`n$body`nendobj`n")
}
Add-PdfObject 1 '<< /Type /Catalog /Pages 2 0 R >>'
Add-PdfObject 2 '<< /Type /Pages /Kids [3 0 R] /Count 1 >>'
Add-PdfObject 3 '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Contents 4 0 R >>'
Add-PdfObject 4 "<< /Length 0 >>`nstream`n`nendstream"
$xref = $encoding.GetByteCount($builder.ToString())
$null = $builder.Append("xref`n0 5`n0000000000 65535 f `n")
foreach ($number in 1..4) {
    $null = $builder.Append(('{0:D10} 00000 n ' -f $offsets[$number]) + "`n")
}
$null = $builder.Append("trailer`n<< /Size 5 /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
$pdf = $encoding.GetBytes($builder.ToString())

$pdfPin = [Runtime.InteropServices.GCHandle]::Alloc($pdf, [Runtime.InteropServices.GCHandleType]::Pinned)
$document = [IntPtr]::Zero
try {
    if ([HSPdfPdfiumSmokeNative]::HSPDF_Initialize() -eq 0) { throw 'PDFium initialization failed.' }
    $document = [HSPdfPdfiumSmokeNative]::HSPDF_OpenDocumentMemory($pdfPin.AddrOfPinnedObject(), [uint64]$pdf.LongLength)
    if ($document -eq [IntPtr]::Zero) { throw 'PDFium could not open the smoke-test PDF.' }
    if ([HSPdfPdfiumSmokeNative]::HSPDF_GetPageCount($document) -ne 1) { throw 'Unexpected PDFium page count.' }

    [double]$width = 0
    [double]$height = 0
    if ([HSPdfPdfiumSmokeNative]::HSPDF_GetPageSize($document, 0, [ref]$width, [ref]$height) -eq 0 -or $width -le 0 -or $height -le 0) {
        throw 'PDFium page-size query failed.'
    }
    if ([HSPdfPdfiumSmokeNative]::HSPDF_GetAttachmentCount($document) -ne 0) { throw 'Unexpected smoke-test attachment count.' }

    $render = New-Object byte[] (64 * 96 * 4)
    $renderPin = [Runtime.InteropServices.GCHandle]::Alloc($render, [Runtime.InteropServices.GCHandleType]::Pinned)
    try {
        if ([HSPdfPdfiumSmokeNative]::HSPDF_RenderPage($document, 0, 64, 96, 0, 0, $renderPin.AddrOfPinnedObject(), 64 * 4) -eq 0) {
            throw 'PDFium page render failed.'
        }
    } finally {
        $renderPin.Free()
    }
} finally {
    if ($document -ne [IntPtr]::Zero) { [HSPdfPdfiumSmokeNative]::HSPDF_CloseDocument($document) }
    [HSPdfPdfiumSmokeNative]::HSPDF_Shutdown()
    $pdfPin.Free()
}

Write-Host 'PDFium native runtime smoke test passed.'
