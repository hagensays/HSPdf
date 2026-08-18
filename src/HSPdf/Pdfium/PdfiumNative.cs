using System;
using System.Runtime.InteropServices;

namespace HSPdf.Pdfium
{
    internal static class PdfiumNative
    {
        private const string DllName = "pdfium.dll";
        private static readonly object InitLock = new object();
        private static bool _initialized;
        private static bool _shutdownHooked;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int HSPDF_Initialize();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void HSPDF_Shutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern IntPtr HSPDF_OpenDocument(string path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr HSPDF_OpenDocumentMemory(IntPtr data, ulong length);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void HSPDF_CloseDocument(IntPtr document);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint HSPDF_GetLastError();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HSPDF_GetPageCount(IntPtr document);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HSPDF_GetPageSize(IntPtr document, int pageIndex, out double widthPoints, out double heightPoints);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HSPDF_RenderPage(
            IntPtr document,
            int pageIndex,
            int widthPixels,
            int heightPixels,
            int rotationQuarterTurns,
            int printing,
            IntPtr bgraBuffer,
            int stride);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HSPDF_GetAttachmentCount(IntPtr document);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HSPDF_GetAttachmentName(IntPtr document, int index, IntPtr utf16Buffer, int capacityChars);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong HSPDF_GetAttachmentSize(IntPtr document, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HSPDF_CopyAttachmentData(IntPtr document, int index, IntPtr buffer, ulong capacity);

        internal static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (InitLock)
            {
                if (_initialized)
                {
                    return;
                }
                if (IntPtr.Size != 8)
                {
                    throw new PdfiumUnavailableException("HSPdf v0.4 requires a 64-bit Windows process.");
                }

                try
                {
                    if (HSPDF_Initialize() == 0)
                    {
                        throw new PdfiumUnavailableException("PDFium konnte nicht initialisiert werden.");
                    }
                }
                catch (DllNotFoundException exception)
                {
                    throw new PdfiumUnavailableException("pdfium.dll fehlt neben HSPdf.exe.", exception);
                }
                catch (BadImageFormatException exception)
                {
                    throw new PdfiumUnavailableException("pdfium.dll ist nicht die passende x64-Version.", exception);
                }
                catch (EntryPointNotFoundException exception)
                {
                    throw new PdfiumUnavailableException("pdfium.dll passt nicht zu dieser HSPdf-Version.", exception);
                }

                _initialized = true;
                if (!_shutdownHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += delegate { Shutdown(); };
                    _shutdownHooked = true;
                }
            }
        }

        private static void Shutdown()
        {
            lock (InitLock)
            {
                if (!_initialized)
                {
                    return;
                }
                try
                {
                    HSPDF_Shutdown();
                }
                catch
                {
                    // Process shutdown must not surface native cleanup errors.
                }
                _initialized = false;
            }
        }
    }

    internal sealed class PdfiumUnavailableException : Exception
    {
        public PdfiumUnavailableException(string message) : base(message) { }
        public PdfiumUnavailableException(string message, Exception innerException) : base(message, innerException) { }
    }
}
