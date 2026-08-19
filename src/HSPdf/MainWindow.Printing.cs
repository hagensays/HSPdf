using HSPdf.Pdfium;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace HSPdf
{
    public partial class MainWindow
    {
        private bool _printInProgress;

        private void PrintOriginalButton_Click(object sender, RoutedEventArgs e)
        {
            PrintButton_Click(sender, e);
        }

        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null || string.IsNullOrWhiteSpace(_documentPath) || _printInProgress)
            {
                return;
            }

            await StartModernPrintAsync(
                new[] { _documentPath },
                Path.GetFileName(_documentPath),
                false);
        }

        private async void PrintAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null || string.IsNullOrWhiteSpace(_documentPath) || _printInProgress)
            {
                return;
            }

            string directory = Path.GetDirectoryName(_documentPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                List<string> pdfPaths = Directory.EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), NaturalStringComparer.Instance)
                    .ToList();
                if (pdfPaths.Count == 0)
                {
                    StatusTextBlock.Text = "Keine PDFs im Ordner";
                    return;
                }

                await StartModernPrintAsync(pdfPaths, "HSPdf – Alle PDFs", true);
            }
            catch (Exception)
            {
                StatusTextBlock.Text = "Alle drucken fehlgeschlagen";
                MessageBox.Show(this,
                    "Die PDF-Druckfolge konnte nicht gestartet werden.",
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task StartModernPrintAsync(
            IEnumerable<string> paths,
            string jobName,
            bool printAll)
        {
            if (_printInProgress)
            {
                return;
            }

            string[] files = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            if (files.Length == 0)
            {
                return;
            }

            _printInProgress = true;
            PrintButton.IsEnabled = false;
            PrintAllButton.IsEnabled = false;

            try
            {
                using (var session = new PdfiumPrintSession())
                {
                    foreach (string path in files)
                    {
                        session.AddFile(path);
                    }

                    StatusTextBlock.Text = printAll
                        ? "Windows-Druckdialog wird geöffnet…"
                        : "Druckdialog wird geöffnet…";

                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    session.Begin(hwnd, jobName);

                    while (session.State == PdfiumPrintSessionState.Active)
                    {
                        await Task.Delay(150);
                    }

                    if (session.State == PdfiumPrintSessionState.Error)
                    {
                        throw session.CreateError();
                    }

                    switch (session.Completion)
                    {
                        case PdfiumPrintCompletion.Submitted:
                            StatusTextBlock.Text = session.SkippedCount == 0
                                ? "Druckauftrag gesendet"
                                : string.Format("Druckauftrag gesendet · {0} übersprungen", session.SkippedCount);
                            break;
                        case PdfiumPrintCompletion.Canceled:
                        case PdfiumPrintCompletion.Abandoned:
                            StatusTextBlock.Text = "Drucken abgebrochen";
                            break;
                        default:
                            throw new InvalidOperationException("Windows meldet einen fehlgeschlagenen Druckauftrag.");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = printAll
                    ? "Alle drucken fehlgeschlagen"
                    : "Drucken fehlgeschlagen";
                MessageBox.Show(this,
                    string.Format(
                        "Der moderne Windows-Druckdialog oder der PDFium-Druckauftrag ist fehlgeschlagen.\n\nFehler: 0x{0:X8}\n{1}",
                        ex.HResult,
                        ex.Message),
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _printInProgress = false;
                PrintButton.IsEnabled = _document != null;
                PrintAllButton.IsEnabled = _document != null && !string.IsNullOrWhiteSpace(_documentPath);
            }
        }
    }
}
