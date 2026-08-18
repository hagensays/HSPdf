using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;

namespace HSPdf.Pdfium
{
    internal static class PdfiumAttachmentDisplay
    {
        private sealed class CacheEntry
        {
            public long Length;
            public long LastWriteTicks;
            public string[] Names;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> NameCache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static string FormatTree(IEnumerable<PdfiumAttachment> attachments)
        {
            if (attachments == null)
            {
                return string.Empty;
            }

            return FormatTree(attachments
                .Select(item => item.Name)
                .OrderBy(name => name, NaturalStringComparer.Instance));
        }

        public static string FormatTree(IEnumerable<string> names)
        {
            if (names == null)
            {
                return string.Empty;
            }

            string[] ordered = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, NaturalStringComparer.Instance)
                .ToArray();
            if (ordered.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int index = 0; index < ordered.Length; index++)
            {
                if (index > 0)
                {
                    builder.AppendLine();
                }
                builder.Append(index + 1 == ordered.Length ? "└─ " : "├─ ");
                builder.Append(ordered[index]);
            }
            return builder.ToString();
        }

        public static string FormatFileTree(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                var info = new FileInfo(path);
                lock (CacheLock)
                {
                    CacheEntry cached;
                    if (NameCache.TryGetValue(path, out cached) &&
                        cached.Length == info.Length &&
                        cached.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
                    {
                        return FormatTree(cached.Names);
                    }
                }

                string[] names;
                using (PdfiumDocument document = PdfiumDocument.Open(path))
                {
                    names = document.GetPdfAttachments(false)
                        .Select(item => item.Name)
                        .OrderBy(name => name, NaturalStringComparer.Instance)
                        .ToArray();
                }

                lock (CacheLock)
                {
                    if (NameCache.Count > 128)
                    {
                        NameCache.Clear();
                    }
                    NameCache[path] = new CacheEntry
                    {
                        Length = info.Length,
                        LastWriteTicks = info.LastWriteTimeUtc.Ticks,
                        Names = names
                    };
                }
                return FormatTree(names);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public sealed class PdfiumAttachmentTreeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return PdfiumAttachmentDisplay.FormatFileTree(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
