using HSPdf.Pdfium;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HSPdf
{
    public partial class MainWindow
    {
        private sealed class PrintPdfHandle : IDisposable
        {
            public string Name;
            public PdfiumDocument Document;
            public bool OwnsDocument;

            public void Dispose()
            {
                if (OwnsDocument && Document != null)
                {
                    Document.Dispose();
                }
                Document = null;
            }
        }

        private sealed class PdfSequencePaginator : DocumentPaginator
        {
            private const double PrintMargin = 24.0;
            private const double PrintDpi = 300.0;
            private const double MaxPrintRenderPixels = 32000000.0;

            private sealed class Entry
            {
                public PdfiumDocument Document;
                public int StartPage;
                public int PageCount;
            }

            private readonly List<Entry> _entries = new List<Entry>();
            private readonly int _pageCount;
            private Size _pageSize;

            public PdfSequencePaginator(IEnumerable<PdfiumDocument> documents, Size pageSize)
            {
                _pageSize = pageSize;
                int startPage = 0;
                foreach (PdfiumDocument document in documents)
                {
                    if (document == null || document.PageCount == 0)
                    {
                        continue;
                    }

                    int count = checked((int)document.PageCount);
                    _entries.Add(new Entry
                    {
                        Document = document,
                        StartPage = startPage,
                        PageCount = count
                    });
                    startPage = checked(startPage + count);
                }
                _pageCount = startPage;
            }

            public override bool IsPageCountValid { get { return true; } }
            public override int PageCount { get { return _pageCount; } }
            public override Size PageSize { get { return _pageSize; } set { _pageSize = value; } }
            public override IDocumentPaginatorSource Source { get { return null; } }

            public override DocumentPage GetPage(int pageNumber)
            {
                if (pageNumber < 0 || pageNumber >= _pageCount)
                {
                    return DocumentPage.Missing;
                }

                Entry entry = _entries.FirstOrDefault(candidate =>
                    pageNumber >= candidate.StartPage &&
                    pageNumber < candidate.StartPage + candidate.PageCount);
                if (entry == null)
                {
                    return DocumentPage.Missing;
                }

                uint localPage = checked((uint)(pageNumber - entry.StartPage));
                Size pdfSize = entry.Document.GetPageSizeDip(localPage);
                double availableWidth = Math.Max(1.0, PageSize.Width - (PrintMargin * 2.0));
                double availableHeight = Math.Max(1.0, PageSize.Height - (PrintMargin * 2.0));
                double scale = Math.Min(availableWidth / pdfSize.Width, availableHeight / pdfSize.Height);
                double drawWidth = pdfSize.Width * scale;
                double drawHeight = pdfSize.Height * scale;

                double renderScale = PrintDpi / 96.0;
                double renderWidth = drawWidth * renderScale;
                double renderHeight = drawHeight * renderScale;
                double pixels = renderWidth * renderHeight;
                if (pixels > MaxPrintRenderPixels)
                {
                    double correction = Math.Sqrt(MaxPrintRenderPixels / pixels);
                    renderWidth *= correction;
                    renderHeight *= correction;
                }

                BitmapSource bitmap = entry.Document.RenderPageAsync(
                    localPage,
                    checked((int)Math.Max(1.0, Math.Round(renderWidth))),
                    checked((int)Math.Max(1.0, Math.Round(renderHeight))),
                    0,
                    true).GetAwaiter().GetResult();

                double x = (PageSize.Width - drawWidth) / 2.0;
                double y = (PageSize.Height - drawHeight) / 2.0;
                var visual = new DrawingVisual();
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White, null, new Rect(new Point(0, 0), PageSize));
                    drawing.DrawImage(bitmap, new Rect(x, y, drawWidth, drawHeight));
                }

                return new DocumentPage(
                    visual,
                    PageSize,
                    new Rect(new Point(0, 0), PageSize),
                    new Rect(x, y, drawWidth, drawHeight));
            }
        }

        private void PrintOriginalButton_Click(object sender, RoutedEventArgs e)
        {
            PrintButton_Click(sender, e);
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            try
            {
                var handles = new[]
                {
                    new PrintPdfHandle
                    {
                        Name = Path.GetFileName(_documentPath),
                        Document = _document,
                        OwnsDocument = false
                    }
                };
                PrintSequence(handles, Path.GetFileName(_documentPath), "Druckauftrag gesendet");
            }
            catch (Exception)
            {
                StatusTextBlock.Text = "Drucken fehlgeschlagen";
                MessageBox.Show(this,
                    "Das Dokument konnte nicht gedruckt werden.",
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void PrintAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null || string.IsNullOrWhiteSpace(_documentPath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(_documentPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var handles = new List<PrintPdfHandle>();
            int skipped = 0;
            try
            {
                List<string> pdfPaths = Directory.EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), NaturalStringComparer.Instance)
                    .ToList();
                if (pdfPaths.Count == 0)
                {
                    return;
                }

                for (int index = 0; index < pdfPaths.Count; index++)
                {
                    string path = pdfPaths[index];
                    StatusTextBlock.Text = string.Format("Druckfolge wird vorbereitet… {0}/{1}", index + 1, pdfPaths.Count);

                    PdfiumDocument parentDocument;
                    try
                    {
                        parentDocument = await Task.Run(() => PdfiumDocument.Open(path));
                    }
                    catch
                    {
                        skipped++;
                        continue;
                    }

                    handles.Add(new PrintPdfHandle
                    {
                        Name = Path.GetFileName(path),
                        Document = parentDocument,
                        OwnsDocument = true
                    });

                    IReadOnlyList<PdfiumAttachment> attachments;
                    try
                    {
                        attachments = await Task.Run(() =>
                            (IReadOnlyList<PdfiumAttachment>)parentDocument.GetPdfAttachments(true)
                                .OrderBy(item => item.Name, NaturalStringComparer.Instance)
                                .ToArray());
                    }
                    catch
                    {
                        skipped++;
                        continue;
                    }

                    foreach (PdfiumAttachment attachment in attachments)
                    {
                        try
                        {
                            PdfiumDocument childDocument = await Task.Run(() => PdfiumDocument.Open(attachment.Data));
                            handles.Add(new PrintPdfHandle
                            {
                                Name = attachment.Name,
                                Document = childDocument,
                                OwnsDocument = true
                            });
                        }
                        catch
                        {
                            skipped++;
                        }
                    }
                }

                if (handles.Count == 0)
                {
                    StatusTextBlock.Text = "Keine druckbaren PDFs gefunden";
                    return;
                }

                string success = skipped == 0
                    ? string.Format("{0} PDFs an Drucker gesendet", handles.Count)
                    : string.Format("{0} PDFs gesendet · {1} übersprungen", handles.Count, skipped);
                PrintSequence(handles, "HSPdf – Alle PDFs", success);
            }
            catch (Exception)
            {
                StatusTextBlock.Text = "Alle drucken fehlgeschlagen";
                MessageBox.Show(this,
                    "Die PDF-Druckfolge konnte nicht vollständig vorbereitet oder an den Drucker gesendet werden.",
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                foreach (PrintPdfHandle handle in handles)
                {
                    handle.Dispose();
                }
            }
        }

        private void PrintSequence(IEnumerable<PrintPdfHandle> handles, string jobName, string successText)
        {
            var documents = handles.Where(item => item != null && item.Document != null)
                .Select(item => item.Document)
                .ToArray();
            if (documents.Length == 0)
            {
                return;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                StatusTextBlock.Text = "Drucken abgebrochen";
                return;
            }

            double pageWidth = dialog.PrintableAreaWidth;
            double pageHeight = dialog.PrintableAreaHeight;
            if (double.IsNaN(pageWidth) || double.IsInfinity(pageWidth) || pageWidth <= 0) pageWidth = 816.0;
            if (double.IsNaN(pageHeight) || double.IsInfinity(pageHeight) || pageHeight <= 0) pageHeight = 1056.0;

            StatusTextBlock.Text = "Druck wird über PDFium vorbereitet…";
            var paginator = new PdfSequencePaginator(documents, new Size(pageWidth, pageHeight));
            dialog.PrintDocument(paginator, jobName);
            StatusTextBlock.Text = successText;
        }
    }
}
