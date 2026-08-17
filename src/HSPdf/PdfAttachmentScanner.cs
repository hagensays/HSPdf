using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;

namespace HSPdf
{
    internal static class PdfAttachmentScanner
    {
        private const int SegmentBytes = 2 * 1024 * 1024;
        private const int ContextRadius = 2048;
        private const int MaxAttachments = 64;

        public static IReadOnlyList<string> Scan(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<string>();
            }

            try
            {
                string text = ReadSearchText(path);
                if (string.IsNullOrEmpty(text))
                {
                    return Array.Empty<string>();
                }

                var names = new List<string>();
                var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

                ScanAnchors(text, "/Filespec", names, seen);
                ScanAnchors(text, "/EF", names, seen);

                return names;
            }
            catch
            {
                return Array.Empty<string>();
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

        private static void ScanAnchors(string text, string anchor, List<string> names, HashSet<string> seen)
        {
            int index = 0;
            while (index < text.Length && names.Count < MaxAttachments)
            {
                index = text.IndexOf(anchor, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                int start = Math.Max(0, index - ContextRadius);
                int end = Math.Min(text.Length, index + anchor.Length + ContextRadius);
                string context = text.Substring(start, end - start);

                // An embedded-file Filespec normally contains /EF. Requiring either
                // /EF or /EmbeddedFiles avoids treating ordinary external file specs
                // as attachments.
                if (context.IndexOf("/EF", StringComparison.Ordinal) >= 0 ||
                    context.IndexOf("/EmbeddedFiles", StringComparison.Ordinal) >= 0)
                {
                    string name = FindPdfStringAfterName(context, "/UF") ??
                                  FindPdfStringAfterName(context, "/F");
                    name = CleanName(name);
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }

                index += anchor.Length;
            }
        }

        private static string ReadSearchText(string path)
        {
            var info = new FileInfo(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (info.Length <= SegmentBytes * 2L)
                {
                    byte[] all = ReadBytes(stream, checked((int)info.Length));
                    return Encoding.GetEncoding(28591).GetString(all);
                }

                byte[] first = ReadBytes(stream, SegmentBytes);
                stream.Seek(Math.Max(0L, info.Length - SegmentBytes), SeekOrigin.Begin);
                byte[] last = ReadBytes(stream, SegmentBytes);

                return Encoding.GetEncoding(28591).GetString(first) +
                       "\n% HSPdf segment boundary %\n" +
                       Encoding.GetEncoding(28591).GetString(last);
            }
        }

        private static byte[] ReadBytes(Stream stream, int requested)
        {
            var buffer = new byte[requested];
            int offset = 0;
            while (offset < requested)
            {
                int read = stream.Read(buffer, offset, requested - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }

            if (offset == buffer.Length)
            {
                return buffer;
            }

            Array.Resize(ref buffer, offset);
            return buffer;
        }

        private static string FindPdfStringAfterName(string context, string token)
        {
            int tokenIndex = 0;
            while (tokenIndex < context.Length)
            {
                tokenIndex = context.IndexOf(token, tokenIndex, StringComparison.Ordinal);
                if (tokenIndex < 0)
                {
                    return null;
                }

                int position = tokenIndex + token.Length;
                while (position < context.Length && char.IsWhiteSpace(context[position]))
                {
                    position++;
                }

                if (position < context.Length && context[position] == '(')
                {
                    return ParseLiteralString(context, position);
                }

                if (position < context.Length && context[position] == '<' &&
                    (position + 1 >= context.Length || context[position + 1] != '<'))
                {
                    return ParseHexString(context, position);
                }

                tokenIndex += token.Length;
            }

            return null;
        }

        private static string ParseLiteralString(string text, int openIndex)
        {
            var bytes = new List<byte>();
            int depth = 1;

            for (int index = openIndex + 1; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '\\')
                {
                    if (++index >= text.Length)
                    {
                        break;
                    }

                    char escaped = text[index];
                    switch (escaped)
                    {
                        case 'n': bytes.Add((byte)'\n'); break;
                        case 'r': bytes.Add((byte)'\r'); break;
                        case 't': bytes.Add((byte)'\t'); break;
                        case 'b': bytes.Add(8); break;
                        case 'f': bytes.Add(12); break;
                        case '\r':
                            if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                            break;
                        case '\n':
                            break;
                        default:
                            if (escaped >= '0' && escaped <= '7')
                            {
                                int value = escaped - '0';
                                int count = 1;
                                while (count < 3 && index + 1 < text.Length && text[index + 1] >= '0' && text[index + 1] <= '7')
                                {
                                    value = (value * 8) + (text[++index] - '0');
                                    count++;
                                }
                                bytes.Add((byte)(value & 0xFF));
                            }
                            else
                            {
                                bytes.Add((byte)escaped);
                            }
                            break;
                    }
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                }
                else if (current == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return DecodePdfBytes(bytes.ToArray());
                    }
                }

                if (depth > 0 && current <= byte.MaxValue)
                {
                    bytes.Add((byte)current);
                }
            }

            return null;
        }

        private static string ParseHexString(string text, int openIndex)
        {
            var hex = new StringBuilder();
            for (int index = openIndex + 1; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '>')
                {
                    break;
                }
                if (Uri.IsHexDigit(current))
                {
                    hex.Append(current);
                }
            }

            if (hex.Length == 0)
            {
                return null;
            }
            if ((hex.Length & 1) != 0)
            {
                hex.Append('0');
            }

            var bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = byte.Parse(hex.ToString(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return DecodePdfBytes(bytes);
        }

        private static string DecodePdfBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            return Encoding.GetEncoding(28591).GetString(bytes);
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string cleaned = value.Replace("\0", string.Empty).Trim();
            if (cleaned.Length > 180)
            {
                cleaned = cleaned.Substring(0, 177) + "…";
            }
            return cleaned;
        }
    }

    public sealed class PdfAttachmentTreeConverter : IValueConverter
    {
        private sealed class CacheEntry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public string Tree;
        }

        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                var info = new FileInfo(path);
                CacheEntry entry;
                lock (Cache)
                {
                    if (Cache.TryGetValue(path, out entry) &&
                        entry.Length == info.Length && entry.LastWriteUtc == info.LastWriteTimeUtc)
                    {
                        return entry.Tree;
                    }
                }

                string tree = PdfAttachmentScanner.FormatTree(PdfAttachmentScanner.Scan(path));
                lock (Cache)
                {
                    if (Cache.Count >= 128)
                    {
                        Cache.Clear();
                    }
                    Cache[path] = new CacheEntry
                    {
                        Length = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc,
                        Tree = tree
                    };
                }
                return tree;
            }
            catch
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
