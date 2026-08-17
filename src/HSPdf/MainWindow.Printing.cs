using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace HSPdf
{
    public partial class MainWindow
    {
        private sealed class PrintPdfHandle : IDisposable
        {
            public string Name;
            public PdfDocument Document;
            public Stream BackingStream;
            public IRandomAccessStream RandomAccessStream;

            public void Dispose()
            {
                if (RandomAccessStream != null)
                {
                    RandomAccessStream.Dispose();
                    RandomAccessStream = null;
                }

                if (BackingStream != null)
                {
                    BackingStream.Dispose();
                    BackingStream = null;
                }
            }
        }

        private sealed class PdfSequencePaginator : DocumentPaginator
        {
            private const double PrintMargin = 24.0;
            private const double PrintRenderScale = 2.0;
            private const double MaxPrintRenderPixels = 16000000.0;

            private sealed class Entry
            {
                public PdfDocument Document;
                public int StartPage;
                public int PageCount;
            }

            private readonly List<Entry> _entries = new List<Entry>();
            private readonly int _pageCount;
            private Size _pageSize;

            public PdfSequencePaginator(IEnumerable<PdfDocument> documents, Size pageSize)
            {
                _pageSize = pageSize;
                int startPage = 0;

                foreach (PdfDocument document in documents)
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

            public override bool IsPageCountValid
            {
                get { return true; }
            }

            public override int PageCount
            {
                get { return _pageCount; }
            }

            public override Size PageSize
            {
                get { return _pageSize; }
                set { _pageSize = value; }
            }

            public override IDocumentPaginatorSource Source
            {
                get { return null; }
            }

            public override DocumentPage GetPage(int pageNumber)
            {
                if (pageNumber < 0 || pageNumber >= _pageCount)
                {
                    return DocumentPage.Missing;
                }

                Entry entry = null;
                for (int index = 0; index < _entries.Count; index++)
                {
                    Entry candidate = _entries[index];
                    if (pageNumber >= candidate.StartPage && pageNumber < candidate.StartPage + candidate.PageCount)
                    {
                        entry = candidate;
                        break;
                    }
                }

                if (entry == null)
                {
                    return DocumentPage.Missing;
                }

                int localPage = pageNumber - entry.StartPage;
                using (PdfPage page = entry.Document.GetPage((uint)localPage))
                {
                    double availableWidth = Math.Max(1.0, PageSize.Width - (PrintMargin * 2.0));
                    double availableHeight = Math.Max(1.0, PageSize.Height - (PrintMargin * 2.0));
                    double pageWidth = Math.Max(1.0, page.Size.Width);
                    double pageHeight = Math.Max(1.0, page.Size.Height);
                    double scale = Math.Min(availableWidth / pageWidth, availableHeight / pageHeight);
                    double drawWidth = pageWidth * scale;
                    double drawHeight = pageHeight * scale;

                    double renderWidth = drawWidth * PrintRenderScale;
                    double renderHeight = drawHeight * PrintRenderScale;
                    double pixels = renderWidth * renderHeight;
                    if (pixels > MaxPrintRenderPixels)
                    {
                        double correction = Math.Sqrt(MaxPrintRenderPixels / pixels);
                        renderWidth *= correction;
                        renderHeight *= correction;
                    }

                    BitmapSource bitmap = RenderPageBitmapAsync(
                        page,
                        (uint)Math.Max(1.0, Math.Round(renderWidth)),
                        (uint)Math.Max(1.0, Math.Round(renderHeight))).GetAwaiter().GetResult();

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

                    PrintPdfHandle parent = await OpenPrintPdfFromPathAsync(path);
                    if (parent != null)
                    {
                        handles.Add(parent);
                    }
                    else
                    {
                        skipped++;
                    }

                    IReadOnlyList<PdfEmbeddedAttachment> attachments = PdfAttachmentScanner.ExtractPdfAttachments(path);
                    foreach (PdfEmbeddedAttachment attachment in attachments)
                    {
                        PrintPdfHandle child = await OpenPrintPdfFromBytesAsync(attachment.Name, attachment.Data);
                        if (child != null)
                        {
                            handles.Add(child);
                        }
                        else
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

                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true)
                {
                    StatusTextBlock.Text = "Drucken abgebrochen";
                    return;
                }

                double pageWidth = dialog.PrintableAreaWidth;
                double pageHeight = dialog.PrintableAreaHeight;
                if (double.IsNaN(pageWidth) || double.IsInfinity(pageWidth) || pageWidth <= 0)
                {
                    pageWidth = 816.0;
                }
                if (double.IsNaN(pageHeight) || double.IsInfinity(pageHeight) || pageHeight <= 0)
                {
                    pageHeight = 1056.0;
                }

                StatusTextBlock.Text = string.Format("{0} PDFs werden gedruckt…", handles.Count);
                var paginator = new PdfSequencePaginator(
                    handles.Select(item => item.Document),
                    new Size(pageWidth, pageHeight));

                dialog.PrintDocument(paginator, "HSPdf – Alle PDFs");
                StatusTextBlock.Text = skipped == 0
                    ? string.Format("{0} PDFs an Drucker gesendet", handles.Count)
                    : string.Format("{0} PDFs gesendet · {1} übersprungen", handles.Count, skipped);
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

        private static async Task<PrintPdfHandle> OpenPrintPdfFromPathAsync(string path)
        {
            FileStream fileStream = null;
            IRandomAccessStream randomAccessStream = null;
            try
            {
                fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                randomAccessStream = fileStream.AsRandomAccessStream();
                PdfDocument document = await PdfDocument.LoadFromStreamAsync(randomAccessStream).AsTask();
                if (document.PageCount == 0)
                {
                    randomAccessStream.Dispose();
                    fileStream.Dispose();
                    return null;
                }

                return new PrintPdfHandle
                {
                    Name = Path.GetFileName(path),
                    Document = document,
                    BackingStream = fileStream,
                    RandomAccessStream = randomAccessStream
                };
            }
            catch
            {
                if (randomAccessStream != null) randomAccessStream.Dispose();
                if (fileStream != null) fileStream.Dispose();
                return null;
            }
        }

        private static async Task<PrintPdfHandle> OpenPrintPdfFromBytesAsync(string name, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            MemoryStream memoryStream = null;
            IRandomAccessStream randomAccessStream = null;
            try
            {
                memoryStream = new MemoryStream(data, false);
                randomAccessStream = memoryStream.AsRandomAccessStream();
                PdfDocument document = await PdfDocument.LoadFromStreamAsync(randomAccessStream).AsTask();
                if (document.PageCount == 0)
                {
                    randomAccessStream.Dispose();
                    memoryStream.Dispose();
                    return null;
                }

                return new PrintPdfHandle
                {
                    Name = name,
                    Document = document,
                    BackingStream = memoryStream,
                    RandomAccessStream = randomAccessStream
                };
            }
            catch
            {
                if (randomAccessStream != null) randomAccessStream.Dispose();
                if (memoryStream != null) memoryStream.Dispose();
                return null;
            }
        }
    }
}
