using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HSPdf.Pdfium
{
    internal enum PdfiumPrintSessionState
    {
        Idle = 0,
        Active = 1,
        Completed = 2,
        Error = 3
    }

    internal enum PdfiumPrintCompletion
    {
        Abandoned = 0,
        Canceled = 1,
        Failed = 2,
        Submitted = 3
    }

    internal sealed class PdfiumPrintSession : IDisposable
    {
        private const string DllName = "pdfium.dll";
        private IntPtr _handle;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr HSPDF_CreatePrintSession();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int HSPDF_PrintSessionAddFile(IntPtr session, string path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int HSPDF_BeginModernPrint(IntPtr session, IntPtr ownerHwnd, string title);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int HSPDF_GetModernPrintState(IntPtr session);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int HSPDF_GetModernPrintError(IntPtr session);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int HSPDF_GetModernPrintCompletion(IntPtr session);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int HSPDF_GetModernPrintSkippedCount(IntPtr session);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void HSPDF_DestroyPrintSession(IntPtr session);

        public PdfiumPrintSession()
        {
            PdfiumNative.EnsureInitialized();
            _handle = HSPDF_CreatePrintSession();
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("PDFium-Drucksitzung konnte nicht erstellt werden.");
            }
        }

        public PdfiumPrintSessionState State
        {
            get
            {
                EnsureNotDisposed();
                return (PdfiumPrintSessionState)HSPDF_GetModernPrintState(_handle);
            }
        }

        public PdfiumPrintCompletion Completion
        {
            get
            {
                EnsureNotDisposed();
                return (PdfiumPrintCompletion)HSPDF_GetModernPrintCompletion(_handle);
            }
        }

        public int SkippedCount
        {
            get
            {
                EnsureNotDisposed();
                return Math.Max(0, HSPDF_GetModernPrintSkippedCount(_handle));
            }
        }

        public void AddFile(string path)
        {
            EnsureNotDisposed();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("PDF-Pfad fehlt.", "path");
            }
            if (HSPDF_PrintSessionAddFile(_handle, path) == 0)
            {
                throw new InvalidOperationException("PDF konnte der Drucksitzung nicht hinzugefügt werden.");
            }
        }

        public void Begin(IntPtr ownerHwnd, string title)
        {
            EnsureNotDisposed();
            int result = HSPDF_BeginModernPrint(_handle, ownerHwnd, title ?? "HSPdf");
            if (result < 0)
            {
                throw new Win32Exception(result, "Der moderne Windows-Druckdialog konnte nicht geöffnet werden.");
            }
        }

        public Exception CreateError()
        {
            EnsureNotDisposed();
            int error = HSPDF_GetModernPrintError(_handle);
            if (error == 0)
            {
                return new InvalidOperationException("Der Windows-Druckauftrag ist fehlgeschlagen.");
            }
            return Marshal.GetExceptionForHR(error) ??
                new Win32Exception(error, "Der Windows-Druckauftrag ist fehlgeschlagen.");
        }

        public void Dispose()
        {
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                HSPDF_DestroyPrintSession(handle);
            }
            GC.SuppressFinalize(this);
        }

        ~PdfiumPrintSession()
        {
            Dispose();
        }

        private void EnsureNotDisposed()
        {
            if (_handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException("PdfiumPrintSession");
            }
        }
    }
}
