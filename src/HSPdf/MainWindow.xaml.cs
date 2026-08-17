using HSPdf.Infrastructure;
using HSPdf.Pdfium;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HSPdf
{
    public partial class MainWindow : Window
    {
        private enum ViewMode
        {
            FitHeight,
            FitWidth,
            Manual
        }

        private sealed class FolderPdfItem
        {
            public FolderPdfItem(string path)
            {
                Path = path;
                Name = System.IO.Path.GetFileName(path);
            }

            public string Name { get; private set; }
            public string Path { get; private set; }
        }

        private const double MinimumManualZoom = 0.10;
        private const double MaximumZoom = 4.0;
        private const double ZoomStep = 1.15;
        private const double MaxRenderPixels = 16000000.0;
        private const int MaxCachedPages = 3;
        private const double MinimumSidebarWidth = 120.0;
        private const double MaximumSidebarWidth = 360.0;
        private const double MinimumViewerWidth = 320.0;

        private readonly SemaphoreSlim _renderGate = new SemaphoreSlim(1, 1);
        private readonly DispatcherTimer _resizeTimer;
        private readonly Dictionary<string, BitmapSource> _renderCache = new Dictionary<string, BitmapSource>();
        private readonly Queue<string> _cacheOrder = new Queue<string>();

        private PdfiumDocument _document;
        private string _documentPath;
        private uint _pageIndex;
        private ViewMode _viewMode = ViewMode.FitHeight;
        private double _manualZoom = 1.0;
        private double _effectiveZoom = 1.0;
        private int _rotationDegrees;
        private int _renderRequest;
        private bool _isResizingSidebar;
        private bool _resizeFromLeft;

        public MainWindow()
        {
            InitializeComponent();
            AppNameTextBlock.Text = SuiteInfo.DisplayName;
            AppDescriptionTextBlock.Text = SuiteInfo.Description;
            VersionTextBlock.Text = VersionInfo.DisplayVersion;

            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _resizeTimer.Tick += ResizeTimer_Tick;
            UpdateUiState();
            RefreshFolderPdfList();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string startupPdf = Environment.GetCommandLineArgs().Skip(1)
                .FirstOrDefault(path => File.Exists(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(startupPdf))
            {
                await OpenPdfAsync(startupPdf);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _resizeTimer.Stop();
            Interlocked.Increment(ref _renderRequest);
            DisposeDocument();
            _renderGate.Dispose();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (LeftSidebarColumn == null || ShellGrid == null || ShellGrid.ActualWidth <= 0)
            {
                return;
            }

            double currentWidth = LeftSidebarColumn.ActualWidth > 0
                ? LeftSidebarColumn.ActualWidth
                : LeftSidebarColumn.Width.Value;
            SetSidebarWidth(currentWidth);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string path = ((string[])e.Data.GetData(DataFormats.FileDrop))
                .FirstOrDefault(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(path))
            {
                await OpenPdfAsync(path);
            }
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF-Dateien (*.pdf)|*.pdf|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "PDF öffnen"
            };

            if (dialog.ShowDialog(this) == true)
            {
                await OpenPdfAsync(dialog.FileName);
            }
        }

        private async Task OpenPdfAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            StatusTextBlock.Text = "PDF wird mit PDFium geöffnet…";
            PdfiumDocument document = null;
            try
            {
                document = await Task.Run(() => PdfiumDocument.Open(path));

                Interlocked.Increment(ref _renderRequest);
                DisposeDocument();
                _document = document;
                document = null;
                _documentPath = path;
                _pageIndex = 0;
                _viewMode = ViewMode.FitHeight;
                _manualZoom = 1.0;
                _effectiveZoom = 1.0;
                _rotationDegrees = 0;
                ClearCache();
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                PageBorder.Visibility = Visibility.Visible;
                RefreshFolderPdfList();
                UpdateUiState();
                await RenderCurrentPageAsync();
            }
            catch (PdfiumUnavailableException exception)
            {
                document?.Dispose();
                StatusTextBlock.Text = "PDFium ist nicht verfügbar";
                MessageBox.Show(this,
                    exception.Message + "\n\nHSPdf.exe und pdfium.dll müssen im selben Ordner liegen.",
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception)
            {
                document?.Dispose();
                StatusTextBlock.Text = "PDF konnte nicht geöffnet werden";
                MessageBox.Show(this,
                    "Die PDF konnte nicht geöffnet werden. Die Datei ist eventuell passwortgeschützt, beschädigt oder nicht zugreifbar.",
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DisposeDocument()
        {
            PdfiumDocument document = _document;
            _document = null;
            if (document != null)
            {
                document.Dispose();
            }
            ClearCache();
        }

        private void ClearCache()
        {
            _renderCache.Clear();
            _cacheOrder.Clear();
        }

        private async Task RenderCurrentPageAsync()
        {
            PdfiumDocument document = _document;
            if (document == null || _pageIndex >= document.PageCount)
            {
                return;
            }

            int request = Interlocked.Increment(ref _renderRequest);
            StatusTextBlock.Text = string.Format("Seite {0} wird mit PDFium gerendert…", _pageIndex + 1);
            await _renderGate.WaitAsync();

            try
            {
                if (request != _renderRequest || !ReferenceEquals(document, _document))
                {
                    return;
                }

                int width;
                int height;
                double scale;
                Size pageSize = document.GetPageSizeDip(_pageIndex);
                CalculateRenderSize(pageSize, out width, out height, out scale);

                string cacheKey = string.Format("{0}:{1}x{2}:r{3}", _pageIndex, width, height, _rotationDegrees);
                BitmapSource bitmap;
                if (!_renderCache.TryGetValue(cacheKey, out bitmap))
                {
                    bitmap = await document.RenderPageAsync(
                        _pageIndex,
                        width,
                        height,
                        (_rotationDegrees / 90) % 4,
                        false);
                    AddToCache(cacheKey, bitmap);
                }

                if (request != _renderRequest || !ReferenceEquals(document, _document))
                {
                    return;
                }

                _effectiveZoom = scale;
                PdfImage.Source = bitmap;
                PdfImage.LayoutTransform = Transform.Identity;
                PageBorder.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                UpdateUiState();
            }
            catch (ObjectDisposedException)
            {
                // A newer document replaced this render request.
            }
            catch (Exception)
            {
                if (request == _renderRequest)
                {
                    StatusTextBlock.Text = "Seite konnte nicht gerendert werden";
                }
            }
            finally
            {
                _renderGate.Release();
            }
        }

        private void CalculateRenderSize(Size pageSize, out int width, out int height, out double scale)
        {
            double pageWidth = Math.Max(1.0, pageSize.Width);
            double pageHeight = Math.Max(1.0, pageSize.Height);
            bool quarterTurn = (_rotationDegrees % 180) != 0;
            double layoutWidth = quarterTurn ? pageHeight : pageWidth;
            double layoutHeight = quarterTurn ? pageWidth : pageHeight;

            double viewportWidth = PdfScrollViewer.ViewportWidth > 0 ? PdfScrollViewer.ViewportWidth : PdfScrollViewer.ActualWidth;
            double viewportHeight = PdfScrollViewer.ViewportHeight > 0 ? PdfScrollViewer.ViewportHeight : PdfScrollViewer.ActualHeight;
            double availableWidth = Math.Max(120.0, viewportWidth - 36.0);
            double availableHeight = Math.Max(120.0, viewportHeight - 36.0);

            if (_viewMode == ViewMode.FitHeight)
            {
                scale = availableHeight / layoutHeight;
            }
            else if (_viewMode == ViewMode.FitWidth)
            {
                scale = availableWidth / layoutWidth;
            }
            else
            {
                scale = _manualZoom;
            }

            double minimum = _viewMode == ViewMode.Manual ? MinimumManualZoom : 0.05;
            scale = Math.Max(minimum, Math.Min(MaximumZoom, scale));

            double renderWidth = layoutWidth * scale;
            double renderHeight = layoutHeight * scale;
            double pixels = renderWidth * renderHeight;
            if (pixels > MaxRenderPixels)
            {
                double correction = Math.Sqrt(MaxRenderPixels / pixels);
                scale *= correction;
                renderWidth *= correction;
                renderHeight *= correction;
            }

            width = checked((int)Math.Max(1.0, Math.Round(renderWidth)));
            height = checked((int)Math.Max(1.0, Math.Round(renderHeight)));
        }

        private void AddToCache(string key, BitmapSource bitmap)
        {
            if (_renderCache.ContainsKey(key))
            {
                return;
            }

            _renderCache[key] = bitmap;
            _cacheOrder.Enqueue(key);
            while (_cacheOrder.Count > MaxCachedPages)
            {
                string oldest = _cacheOrder.Dequeue();
                _renderCache.Remove(oldest);
            }
        }

        private async Task NavigateAsync(int delta)
        {
            if (_document == null)
            {
                return;
            }

            long target = (long)_pageIndex + delta;
            if (target < 0 || target >= _document.PageCount)
            {
                return;
            }

            _pageIndex = checked((uint)target);
            PdfScrollViewer.ScrollToHome();
            UpdateUiState();
            await RenderCurrentPageAsync();
        }

        private async Task SetZoomAsync(double factor)
        {
            if (_document == null)
            {
                return;
            }

            if (_viewMode != ViewMode.Manual)
            {
                _manualZoom = _effectiveZoom;
                _viewMode = ViewMode.Manual;
            }

            _manualZoom = Math.Max(MinimumManualZoom, Math.Min(MaximumZoom, _manualZoom * factor));
            await RenderCurrentPageAsync();
        }

        private async Task SetViewModeAsync(ViewMode mode)
        {
            if (_document == null)
            {
                return;
            }

            _viewMode = mode;
            PdfScrollViewer.ScrollToHome();
            await RenderCurrentPageAsync();
        }

        private async Task RotateAsync()
        {
            if (_document == null)
            {
                return;
            }

            _rotationDegrees = (_rotationDegrees + 90) % 360;
            PdfScrollViewer.ScrollToHome();
            await RenderCurrentPageAsync();
        }

        private void RefreshFolderPdfList()
        {
            if (string.IsNullOrWhiteSpace(_documentPath))
            {
                FolderHeaderTextBlock.Text = "Kein Ordner";
                FolderPdfListBox.ItemsSource = null;
                FolderPdfListBox.Visibility = Visibility.Collapsed;
                FolderEmptyTextBlock.Text = "Keine weiteren PDFs im Ordner";
                FolderEmptyTextBlock.Visibility = Visibility.Visible;
                return;
            }

            string directory = Path.GetDirectoryName(_documentPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            FolderHeaderTextBlock.Text = GetFolderDisplayName(directory);

            try
            {
                List<FolderPdfItem> items = Directory.EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
                    .Where(path => !string.Equals(path, _documentPath, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => Path.GetFileName(path), NaturalStringComparer.Instance)
                    .Select(path => new FolderPdfItem(path))
                    .ToList();

                FolderPdfListBox.ItemsSource = items;
                FolderPdfListBox.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                FolderEmptyTextBlock.Text = items.Count > 0 ? string.Empty : "Keine weiteren PDFs im Ordner";
                FolderEmptyTextBlock.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception)
            {
                FolderPdfListBox.ItemsSource = null;
                FolderPdfListBox.Visibility = Visibility.Collapsed;
                FolderEmptyTextBlock.Text = "Ordner konnte nicht gelesen werden";
                FolderEmptyTextBlock.Visibility = Visibility.Visible;
            }
        }

        private static string GetFolderDisplayName(string directory)
        {
            string trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? directory : name;
        }

        private async void FolderPdfListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FolderPdfItem item = FolderPdfListBox.SelectedItem as FolderPdfItem;
            if (item == null)
            {
                return;
            }

            FolderPdfListBox.SelectedItem = null;
            await OpenPdfAsync(item.Path);
        }

        private void SetSidebarWidth(double requestedWidth)
        {
            if (ShellGrid == null || LeftSidebarColumn == null || RightSidebarColumn == null)
            {
                return;
            }

            double handles = 10.0;
            double widthLimit = (ShellGrid.ActualWidth - MinimumViewerWidth - handles) / 2.0;
            double maximum = Math.Max(MinimumSidebarWidth, Math.Min(MaximumSidebarWidth, widthLimit));
            double width = Math.Max(MinimumSidebarWidth, Math.Min(maximum, requestedWidth));

            LeftSidebarColumn.Width = new GridLength(width);
            RightSidebarColumn.Width = new GridLength(width);
        }

        private void SidebarResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var handle = sender as UIElement;
            if (handle == null)
            {
                return;
            }

            _isResizingSidebar = true;
            _resizeFromLeft = ReferenceEquals(sender, LeftResizeHandle);
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void SidebarResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizingSidebar || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point point = e.GetPosition(ShellGrid);
            double requestedWidth = _resizeFromLeft ? point.X : ShellGrid.ActualWidth - point.X;
            SetSidebarWidth(requestedWidth);
            e.Handled = true;
        }

        private void SidebarResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var handle = sender as UIElement;
            if (handle != null)
            {
                handle.ReleaseMouseCapture();
            }

            _isResizingSidebar = false;
            e.Handled = true;
        }

        private void UpdateUiState()
        {
            bool hasDocument = _document != null;
            PreviousButton.IsEnabled = hasDocument && _pageIndex > 0;
            NextButton.IsEnabled = hasDocument && _pageIndex + 1 < (_document?.PageCount ?? 0);
            ZoomInButton.IsEnabled = hasDocument;
            ZoomOutButton.IsEnabled = hasDocument;
            FitHeightButton.IsEnabled = hasDocument;
            FitWidthButton.IsEnabled = hasDocument;
            RotateButton.IsEnabled = hasDocument;
            PrintButton.IsEnabled = hasDocument;

            if (!hasDocument)
            {
                StatusTextBlock.Text = "Bereit · PDF öffnen";
                ContextTextBlock.Text = "Kein Dokument";
                return;
            }

            string mode = _viewMode == ViewMode.FitHeight ? "Fit H" : _viewMode == ViewMode.FitWidth ? "Fit W" : "Zoom";
            StatusTextBlock.Text = Path.GetFileName(_documentPath);
            ContextTextBlock.Text = string.Format("Seite {0}/{1} · {2:0}% · {3}",
                _pageIndex + 1, _document.PageCount, _effectiveZoom * 100.0, mode);
        }

        private async void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(-1);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(1);
        }

        private async void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            await SetZoomAsync(ZoomStep);
        }

        private async void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            await SetZoomAsync(1.0 / ZoomStep);
        }

        private async void FitHeightButton_Click(object sender, RoutedEventArgs e)
        {
            await SetViewModeAsync(ViewMode.FitHeight);
        }

        private async void FitWidthButton_Click(object sender, RoutedEventArgs e)
        {
            await SetViewModeAsync(ViewMode.FitWidth);
        }

        private async void RotateButton_Click(object sender, RoutedEventArgs e)
        {
            await RotateAsync();
        }

        private void PdfScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_document == null || _viewMode == ViewMode.Manual)
            {
                return;
            }

            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private async void ResizeTimer_Tick(object sender, EventArgs e)
        {
            _resizeTimer.Stop();
            await RenderCurrentPageAsync();
        }

        private async void PdfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await SetZoomAsync(e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep);
                return;
            }

            if (_viewMode == ViewMode.FitHeight)
            {
                e.Handled = true;
                await NavigateAsync(e.Delta < 0 ? 1 : -1);
                return;
            }

            const double edgeTolerance = 2.0;
            bool noVerticalScroll = PdfScrollViewer.ScrollableHeight <= edgeTolerance;
            bool atTop = PdfScrollViewer.VerticalOffset <= edgeTolerance;
            bool atBottom = PdfScrollViewer.VerticalOffset >= PdfScrollViewer.ScrollableHeight - edgeTolerance;

            if (e.Delta < 0 && (noVerticalScroll || atBottom))
            {
                e.Handled = true;
                await NavigateAsync(1);
            }
            else if (e.Delta > 0 && (noVerticalScroll || atTop))
            {
                e.Handled = true;
                await NavigateAsync(-1);
            }
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (control && e.Key == Key.O)
            {
                e.Handled = true;
                OpenButton_Click(sender, new RoutedEventArgs());
                return;
            }
            if (control && e.Key == Key.P)
            {
                e.Handled = true;
                PrintButton_Click(sender, new RoutedEventArgs());
                return;
            }

            if (_document == null)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.PageUp:
                case Key.Left:
                    e.Handled = true;
                    await NavigateAsync(-1);
                    break;
                case Key.PageDown:
                case Key.Right:
                case Key.Space:
                    e.Handled = true;
                    await NavigateAsync(1);
                    break;
                case Key.OemPlus:
                case Key.Add:
                    e.Handled = true;
                    await SetZoomAsync(ZoomStep);
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    e.Handled = true;
                    await SetZoomAsync(1.0 / ZoomStep);
                    break;
                case Key.H:
                    e.Handled = true;
                    await SetViewModeAsync(ViewMode.FitHeight);
                    break;
                case Key.W:
                    e.Handled = true;
                    await SetViewModeAsync(ViewMode.FitWidth);
                    break;
                case Key.R:
                    e.Handled = true;
                    await RotateAsync();
                    break;
                case Key.Home:
                    if (_pageIndex != 0)
                    {
                        e.Handled = true;
                        _pageIndex = 0;
                        PdfScrollViewer.ScrollToHome();
                        await RenderCurrentPageAsync();
                    }
                    break;
                case Key.End:
                    if (_pageIndex + 1 < _document.PageCount)
                    {
                        e.Handled = true;
                        _pageIndex = _document.PageCount - 1;
                        PdfScrollViewer.ScrollToHome();
                        await RenderCurrentPageAsync();
                    }
                    break;
            }
        }
    }
}
