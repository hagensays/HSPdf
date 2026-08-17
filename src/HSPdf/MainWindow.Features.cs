using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace HSPdf
{
    public partial class MainWindow
    {
        private DispatcherTimer _featureTimer;
        private string _featureDocumentPath;
        private uint _featurePageIndex = uint.MaxValue;
        private uint _featurePageCount = uint.MaxValue;

        private bool _spacePanHeld;
        private bool _spacePanUsed;
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panStartHorizontalOffset;
        private double _panStartVerticalOffset;

        private bool _isFullScreen;
        private WindowStyle _savedWindowStyle;
        private WindowState _savedWindowState;
        private ResizeMode _savedResizeMode;
        private double _savedSidebarWidth = 190.0;

        private void Window_LoadedV030(object sender, RoutedEventArgs e)
        {
            Window_Loaded(sender, e);

            if (_featureTimer == null)
            {
                _featureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                _featureTimer.Tick += FeatureTimer_Tick;
            }

            _featureTimer.Start();
            RefreshFeatureChrome(true);
        }

        private void Window_ClosedV030(object sender, EventArgs e)
        {
            _featureTimer?.Stop();
            Window_Closed(sender, e);
        }

        private void FeatureTimer_Tick(object sender, EventArgs e)
        {
            RefreshFeatureChrome(false);
        }

        private void RefreshFeatureChrome(bool force)
        {
            bool hasDocument = _document != null && !string.IsNullOrWhiteSpace(_documentPath);
            OpenFolderButton.IsEnabled = hasDocument;
            CopyNameButton.IsEnabled = hasDocument;
            CopyPathButton.IsEnabled = hasDocument;
            CurrentPageTextBox.IsEnabled = hasDocument;

            if (!hasDocument)
            {
                if (force || _featureDocumentPath != null)
                {
                    _featureDocumentPath = null;
                    CurrentPdfNameTextBlock.Text = "Keine PDF geöffnet";
                    CurrentPdfAttachmentTextBlock.Text = string.Empty;
                }

                PageCountTextBlock.Text = "/ –";
                if (!CurrentPageTextBox.IsKeyboardFocusWithin)
                {
                    CurrentPageTextBox.Text = string.Empty;
                }
                return;
            }

            if (force || !string.Equals(_featureDocumentPath, _documentPath, StringComparison.OrdinalIgnoreCase))
            {
                _featureDocumentPath = _documentPath;
                CurrentPdfNameTextBlock.Text = Path.GetFileName(_documentPath);
                CurrentPdfNameTextBlock.ToolTip = _documentPath;
                CurrentPdfAttachmentTextBlock.Text = PdfAttachmentScanner.FormatTree(PdfAttachmentScanner.Scan(_documentPath));
            }

            uint pageCount = _document.PageCount;
            if (force || _featurePageIndex != _pageIndex || _featurePageCount != pageCount)
            {
                _featurePageIndex = _pageIndex;
                _featurePageCount = pageCount;
                PageCountTextBlock.Text = string.Format("/ {0}", pageCount);
                if (!CurrentPageTextBox.IsKeyboardFocusWithin)
                {
                    CurrentPageTextBox.Text = (_pageIndex + 1).ToString();
                }
            }
        }

        private async void CurrentPageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _document == null)
            {
                return;
            }

            e.Handled = true;
            int pageNumber;
            if (!int.TryParse(CurrentPageTextBox.Text.Trim(), out pageNumber) ||
                pageNumber < 1 || pageNumber > _document.PageCount)
            {
                CurrentPageTextBox.Text = (_pageIndex + 1).ToString();
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            uint target = (uint)(pageNumber - 1);
            if (target != _pageIndex)
            {
                _pageIndex = target;
                PdfScrollViewer.ScrollToHome();
                UpdateUiState();
                await RenderCurrentPageAsync();
            }

            PdfScrollViewer.Focus();
            RefreshFeatureChrome(true);
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_documentPath))
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + directory + "\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                StatusTextBlock.Text = "Ordner konnte nicht geöffnet werden";
            }
        }

        private void CopyNameButton_Click(object sender, RoutedEventArgs e)
        {
            CopyCurrentPdfText(Path.GetFileName(_documentPath), "Dateiname kopiert");
        }

        private void CopyPathButton_Click(object sender, RoutedEventArgs e)
        {
            CopyCurrentPdfText(_documentPath, "Pfad kopiert");
        }

        private void CopyCurrentPdfText(string value, string successText)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            try
            {
                Clipboard.SetText(value);
                StatusTextBlock.Text = successText;
            }
            catch
            {
                StatusTextBlock.Text = "Zwischenablage nicht verfügbar";
            }
        }

        private async void PdfCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_document == null || _spacePanHeld || e.ClickCount < 2)
            {
                return;
            }

            e.Handled = true;
            await SetViewModeAsync(_viewMode == ViewMode.FitWidth ? ViewMode.FitHeight : ViewMode.FitWidth);
        }

        private void PdfScrollViewer_PreviewMouseDownV030(object sender, MouseButtonEventArgs e)
        {
            bool middlePan = e.ChangedButton == MouseButton.Middle;
            bool spacePan = e.ChangedButton == MouseButton.Left && _spacePanHeld;
            if (!middlePan && !spacePan)
            {
                return;
            }

            if (spacePan)
            {
                _spacePanUsed = true;
            }

            _isPanning = true;
            _panStartPoint = e.GetPosition(PdfScrollViewer);
            _panStartHorizontalOffset = PdfScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = PdfScrollViewer.VerticalOffset;
            PdfScrollViewer.Cursor = Cursors.ScrollAll;
            PdfScrollViewer.CaptureMouse();
            e.Handled = true;
        }

        private void PdfScrollViewer_PreviewMouseMoveV030(object sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            Point current = e.GetPosition(PdfScrollViewer);
            Vector delta = current - _panStartPoint;
            PdfScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset - delta.X);
            PdfScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset - delta.Y);
            e.Handled = true;
        }

        private void PdfScrollViewer_PreviewMouseUpV030(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning || (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left))
            {
                return;
            }

            EndPan();
            e.Handled = true;
        }

        private void PdfScrollViewer_LostMouseCaptureV030(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                EndPan();
            }
        }

        private void EndPan()
        {
            _isPanning = false;
            PdfScrollViewer.ReleaseMouseCapture();
            PdfScrollViewer.Cursor = Cursors.Arrow;
        }

        private async void Window_PreviewKeyDownV030(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                e.Handled = true;
                ToggleFullScreen();
                return;
            }

            if (e.Key == Key.Escape && _isFullScreen)
            {
                e.Handled = true;
                ToggleFullScreen();
                return;
            }

            if (CurrentPageTextBox != null && CurrentPageTextBox.IsKeyboardFocusWithin)
            {
                return;
            }

            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (control && e.Key == Key.P)
            {
                e.Handled = true;
                PrintOriginalButton_Click(sender, new RoutedEventArgs());
                return;
            }

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                {
                    _spacePanHeld = true;
                    _spacePanUsed = false;
                }
                return;
            }

            Window_PreviewKeyDown(sender, e);
        }

        private async void Window_PreviewKeyUpV030(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || !_spacePanHeld)
            {
                return;
            }

            e.Handled = true;
            bool usedForPan = _spacePanUsed;
            _spacePanHeld = false;
            _spacePanUsed = false;

            if (_isPanning)
            {
                EndPan();
            }

            if (!usedForPan && _document != null)
            {
                await NavigateAsync(1);
            }
        }

        private void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                _savedWindowStyle = WindowStyle;
                _savedWindowState = WindowState;
                _savedResizeMode = ResizeMode;
                _savedSidebarWidth = LeftSidebarColumn.ActualWidth > 0
                    ? LeftSidebarColumn.ActualWidth
                    : Math.Max(120.0, LeftSidebarColumn.Width.Value);

                _isFullScreen = true;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LeftSidebarColumn.Width = new GridLength(0);
                    LeftSplitterColumn.Width = new GridLength(0);
                    RightSplitterColumn.Width = new GridLength(0);
                    RightSidebarColumn.Width = new GridLength(0);
                }), DispatcherPriority.Loaded);
            }
            else
            {
                _isFullScreen = false;
                WindowState = WindowState.Normal;
                WindowStyle = _savedWindowStyle;
                ResizeMode = _savedResizeMode;
                WindowState = _savedWindowState;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LeftSplitterColumn.Width = new GridLength(5);
                    RightSplitterColumn.Width = new GridLength(5);
                    SetSidebarWidth(_savedSidebarWidth);
                }), DispatcherPriority.Loaded);
            }
        }
    }
}
