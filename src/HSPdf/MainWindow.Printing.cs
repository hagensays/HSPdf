using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace HSPdf
{
    public partial class MainWindow
    {
        private void Window_PreviewKeyDownV021(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (control && e.Key == Key.P)
            {
                e.Handled = true;
                PrintOriginalButton_Click(sender, new RoutedEventArgs());
                return;
            }

            Window_PreviewKeyDown(sender, e);
        }

        private void PrintOriginalButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null || string.IsNullOrWhiteSpace(_documentPath) || !File.Exists(_documentPath))
            {
                return;
            }

            try
            {
                StatusTextBlock.Text = "Original-PDF wird an Windows übergeben…";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _documentPath,
                    Verb = "print",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(_documentPath) ?? string.Empty
                };

                Process.Start(startInfo);
                StatusTextBlock.Text = "Original-PDF an Druckhandler übergeben";
            }
            catch (Exception)
            {
                StatusTextBlock.Text = "Drucken fehlgeschlagen";
                MessageBox.Show(this,
                    "Windows konnte keinen Druckhandler für die Original-PDF starten. Der verfügbare Druckdialog hängt vom registrierten PDF-Handler dieses Rechners ab.",
                    "HSPdf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
