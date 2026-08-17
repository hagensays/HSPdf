using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HSPdf.Pdfium
{
    internal sealed class PdfiumAttachment
    {
        internal PdfiumAttachment(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        internal string Name { get; private set; }
        internal byte[] Data { get; private set; }
    }

    internal sealed class PdfiumDocument : IDisposable
    {
        private const double PointsToDip = 96.0 / 72.0;
        private const int MaxPdfAttachments = 128;
        private const ulong MaxAttachmentBytes = 128UL * 1024UL * 1024UL;

        private readonly object _lifetimeLock = new object();
        private IntPtr _handle;
        private readonly uint _pageCount;

        private PdfiumDocument(IntPtr handle)
        {
            _handle = handle;
            int count = PdfiumNative.HSPDF_GetPageCount(handle);
            if (count <= 0)
            {
                PdfiumNative.HSPDF_CloseDocument(handle);
                _handle = IntPtr.Zero;
                throw new InvalidDataException("PDF contains no pages.");
            }
            _pageCount = checked((uint)count);
        }

        internal uint PageCount
        {
            get { return _pageCount; }
        }

        internal static PdfiumDocument Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("PDF not found.", path);
            }

            PdfiumNative.EnsureInitialized();
            IntPtr handle = PdfiumNative.HSPDF_OpenDocument(path);
            if (handle == IntPtr.Zero)
            {
                throw CreateOpenException(path);
            }
            return new PdfiumDocument(handle);
        }

        internal static PdfiumDocument Open(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new InvalidDataException("Embedded PDF is empty.");
            }

            PdfiumNative.EnsureInitialized();
            GCHandle pinned = default(GCHandle);
            try
            {
                pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr handle = PdfiumNative.HSPDF_OpenDocumentMemory(pinned.AddrOfPinnedObject(), checked((ulong)data.LongLength));
                if (handle == IntPtr.Zero)
                {
                    throw CreateOpenException("embedded PDF");
                }
                return new PdfiumDocument(handle);
            }
            finally
            {
                if (pinned.IsAllocated)
                {
                    pinned.Free();
                }
            }
        }

        internal Size GetPageSizeDip(uint pageIndex)
        {
            lock (_lifetimeLock)
            {
                EnsureNotDisposed();
                if (pageIndex >= _pageCount)
                {
                    throw new ArgumentOutOfRangeException("pageIndex");
                }

                double widthPoints;
                double heightPoints;
                if (PdfiumNative.HSPDF_GetPageSize(_handle, checked((int)pageIndex), out widthPoints, out heightPoints) == 0 ||
                    widthPoints <= 0 || heightPoints <= 0)
                {
                    throw new InvalidDataException("PDF page size could not be read.");
                }
                return new Size(widthPoints * PointsToDip, heightPoints * PointsToDip);
            }
        }

        internal Task<BitmapSource> RenderPageAsync(
            uint pageIndex,
            int widthPixels,
            int heightPixels,
            int rotationQuarterTurns,
            bool printing)
        {
            if (widthPixels <= 0) throw new ArgumentOutOfRangeException("widthPixels");
            if (heightPixels <= 0) throw new ArgumentOutOfRangeException("heightPixels");

            return Task.Run(() => RenderPage(pageIndex, widthPixels, heightPixels, rotationQuarterTurns, printing));
        }

        private BitmapSource RenderPage(
            uint pageIndex,
            int widthPixels,
            int heightPixels,
            int rotationQuarterTurns,
            bool printing)
        {
            int stride = checked(widthPixels * 4);
            byte[] pixels = new byte[checked(stride * heightPixels)];
            GCHandle pinned = default(GCHandle);

            lock (_lifetimeLock)
            {
                EnsureNotDisposed();
                if (pageIndex >= _pageCount)
                {
                    throw new ArgumentOutOfRangeException("pageIndex");
                }

                try
                {
                    pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    int result = PdfiumNative.HSPDF_RenderPage(
                        _handle,
                        checked((int)pageIndex),
                        widthPixels,
                        heightPixels,
                        rotationQuarterTurns,
                        printing ? 1 : 0,
                        pinned.AddrOfPinnedObject(),
                        stride);
                    if (result == 0)
                    {
                        throw new InvalidDataException("PDFium could not render the page.");
                    }
                }
                finally
                {
                    if (pinned.IsAllocated)
                    {
                        pinned.Free();
                    }
                }
            }

            BitmapSource bitmap = BitmapSource.Create(
                widthPixels,
                heightPixels,
                96.0,
                96.0,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            bitmap.Freeze();
            return bitmap;
        }

        internal IReadOnlyList<PdfiumAttachment> GetPdfAttachments(bool includeData)
        {
            var results = new List<PdfiumAttachment>();
            lock (_lifetimeLock)
            {
                EnsureNotDisposed();
                int count = Math.Min(MaxPdfAttachments, Math.Max(0, PdfiumNative.HSPDF_GetAttachmentCount(_handle)));
                for (int index = 0; index < count; index++)
                {
                    string name = ReadAttachmentName(index);
                    if (string.IsNullOrWhiteSpace(name) ||
                        !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    byte[] data = null;
                    if (includeData)
                    {
                        ulong size = PdfiumNative.HSPDF_GetAttachmentSize(_handle, index);
                        if (size == 0 || size > MaxAttachmentBytes || size > int.MaxValue)
                        {
                            continue;
                        }

                        data = new byte[checked((int)size)];
                        GCHandle pinned = default(GCHandle);
                        try
                        {
                            pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
                            if (PdfiumNative.HSPDF_CopyAttachmentData(
                                    _handle,
                                    index,
                                    pinned.AddrOfPinnedObject(),
                                    size) == 0 ||
                                !LooksLikePdf(data))
                            {
                                continue;
                            }
                        }
                        finally
                        {
                            if (pinned.IsAllocated)
                            {
                                pinned.Free();
                            }
                        }
                    }

                    results.Add(new PdfiumAttachment(name, data));
                }
            }
            return results;
        }

        private string ReadAttachmentName(int index)
        {
            int chars = PdfiumNative.HSPDF_GetAttachmentName(_handle, index, IntPtr.Zero, 0);
            if (chars <= 1 || chars > 32768)
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(checked(chars * 2));
            try
            {
                int copied = PdfiumNative.HSPDF_GetAttachmentName(_handle, index, buffer, chars);
                if (copied <= 1)
                {
                    return null;
                }
                return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0');
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool LooksLikePdf(byte[] data)
        {
            int limit = Math.Min(data.Length - 4, 1024);
            for (int index = 0; index <= limit; index++)
            {
                if (data[index] == (byte)'%' && data[index + 1] == (byte)'P' &&
                    data[index + 2] == (byte)'D' && data[index + 3] == (byte)'F' &&
                    data[index + 4] == (byte)'-')
                {
                    return true;
                }
            }
            return false;
        }

        private static Exception CreateOpenException(string source)
        {
            uint error = PdfiumNative.HSPDF_GetLastError();
            string reason;
            switch (error)
            {
                case 2: reason = "Datei konnte nicht gelesen werden"; break;
                case 3: reason = "ungültiges oder beschädigtes PDF"; break;
                case 4: reason = "passwortgeschütztes PDF"; break;
                case 5: reason = "nicht unterstützte PDF-Sicherheit"; break;
                case 6: reason = "Seitenfehler"; break;
                default: reason = "unbekannter PDF-Fehler"; break;
            }
            return new InvalidDataException(string.Format("PDFium konnte {0} nicht öffnen: {1} (Fehler {2}).", source, reason, error));
        }

        private void EnsureNotDisposed()
        {
            if (_handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException("PdfiumDocument");
            }
        }

        public void Dispose()
        {
            lock (_lifetimeLock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return;
                }
                PdfiumNative.HSPDF_CloseDocument(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
