using HSPdf.Pdfium;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;

namespace HSPdf
{
    internal sealed class PdfEmbeddedAttachment
    {
        public PdfEmbeddedAttachment(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        public string Name { get; private set; }
        public byte[] Data { get; private set; }
    }

    internal sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int left = 0;
            int right = 0;
            while (left < x.Length && right < y.Length)
            {
                bool leftDigit = char.IsDigit(x[left]);
                bool rightDigit = char.IsDigit(y[right]);
                if (leftDigit && rightDigit)
                {
                    int leftEnd = left;
                    int rightEnd = right;
                    while (leftEnd < x.Length && char.IsDigit(x[leftEnd])) leftEnd++;
                    while (rightEnd < y.Length && char.IsDigit(y[rightEnd])) rightEnd++;

                    int leftSignificant = left;
                    int rightSignificant = right;
                    while (leftSignificant < leftEnd - 1 && x[leftSignificant] == '0') leftSignificant++;
                    while (rightSignificant < rightEnd - 1 && y[rightSignificant] == '0') rightSignificant++;

                    int leftDigits = leftEnd - leftSignificant;
                    int rightDigits = rightEnd - rightSignificant;
                    if (leftDigits != rightDigits) return leftDigits.CompareTo(rightDigits);

                    for (int index = 0; index < leftDigits; index++)
                    {
                        int digitCompare = x[leftSignificant + index].CompareTo(y[rightSignificant + index]);
                        if (digitCompare != 0) return digitCompare;
                    }

                    int runCompare = (leftEnd - left).CompareTo(rightEnd - right);
                    if (runCompare != 0) return runCompare;

                    left = leftEnd;
                    right = rightEnd;
                    continue;
                }

                int charCompare = char.ToUpperInvariant(x[left]).CompareTo(char.ToUpperInvariant(y[right]));
                if (charCompare != 0) return charCompare;
                left++;
                right++;
            }

            if (left < x.Length) return 1;
            if (right < y.Length) return -1;
            return StringComparer.CurrentCultureIgnoreCase.Compare(x, y);
        }
    }

    internal static class PdfAttachmentScanner
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

        public static IReadOnlyList<string> Scan(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<string>();
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
                        return cached.Names;
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
                return names;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static IReadOnlyList<PdfEmbeddedAttachment> ExtractPdfAttachments(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<PdfEmbeddedAttachment>();
            }

            try
            {
                using (PdfiumDocument document = PdfiumDocument.Open(path))
                {
                    return document.GetPdfAttachments(true)
                        .OrderBy(item => item.Name, NaturalStringComparer.Instance)
                        .Select(item => new PdfEmbeddedAttachment(item.Name, item.Data))
                        .ToArray();
                }
            }
            catch
            {
                return Array.Empty<PdfEmbeddedAttachment>();
            }
        }

        public static string FormatTree(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int index = 0; index < names.Count; index++)
            {
                if (index > 0)
                {
                    builder.AppendLine();
                }
                builder.Append(index + 1 == names.Count ? "└─ " : "├─ ");
                builder.Append(names[index]);
            }
            return builder.ToString();
        }
    }

    public sealed class PdfAttachmentTreeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string;
            return PdfAttachmentScanner.FormatTree(PdfAttachmentScanner.Scan(path));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
