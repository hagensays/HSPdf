using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        private const long MaxSourceBytes = 128L * 1024L * 1024L;
        private const int MaxObjectStreamBytes = 16 * 1024 * 1024;
        private const int MaxAttachmentBytes = 64 * 1024 * 1024;
        private const int MaxAttachments = 64;

        private sealed class PdfObject
        {
            public int Number;
            public byte[] Body;
            private string _text;

            public string Text
            {
                get
                {
                    if (_text == null)
                    {
                        _text = Encoding.GetEncoding(28591).GetString(Body ?? Array.Empty<byte>());
                    }
                    return _text;
                }
            }
        }

        private sealed class FileSpec
        {
            public string Name;
            public int StreamObjectNumber;
        }

        public static IReadOnlyList<string> Scan(string path)
        {
            return ReadFileSpecs(path, false)
                .Select(item => item.Name)
                .ToArray();
        }

        public static IReadOnlyList<PdfEmbeddedAttachment> ExtractPdfAttachments(string path)
        {
            return ReadFileSpecs(path, true)
                .Where(item => item.Data != null)
                .ToArray();
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

        private static IReadOnlyList<PdfEmbeddedAttachment> ReadFileSpecs(string path, bool includeData)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<PdfEmbeddedAttachment>();
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxSourceBytes || info.Length > int.MaxValue)
                {
                    return Array.Empty<PdfEmbeddedAttachment>();
                }

                byte[] source;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    source = ReadExactly(stream, checked((int)info.Length));
                }

                Dictionary<int, PdfObject> objects = ReadObjects(source);
                ExpandObjectStreams(objects);

                List<FileSpec> specs = FindPdfFileSpecs(objects);
                var results = new List<PdfEmbeddedAttachment>();
                foreach (FileSpec spec in specs)
                {
                    byte[] data = null;
                    if (includeData)
                    {
                        PdfObject streamObject;
                        if (!objects.TryGetValue(spec.StreamObjectNumber, out streamObject))
                        {
                            continue;
                        }

                        data = DecodeStream(streamObject, MaxAttachmentBytes);
                        if (data == null || !LooksLikePdf(data))
                        {
                            continue;
                        }
                    }

                    results.Add(new PdfEmbeddedAttachment(spec.Name, data));
                    if (results.Count >= MaxAttachments)
                    {
                        break;
                    }
                }

                return results;
            }
            catch
            {
                return Array.Empty<PdfEmbeddedAttachment>();
            }
        }

        private static Dictionary<int, PdfObject> ReadObjects(byte[] source)
        {
            string text = Encoding.GetEncoding(28591).GetString(source);
            MatchCollection matches = Regex.Matches(
                text,
                @"(?m)^[ \t]*(\d+)[ \t]+(\d+)[ \t]+obj\b",
                RegexOptions.CultureInvariant);

            var objects = new Dictionary<int, PdfObject>();
            for (int index = 0; index < matches.Count; index++)
            {
                Match match = matches[index];
                int objectNumber;
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out objectNumber))
                {
                    continue;
                }

                int bodyStart = match.Index + match.Length;
                int bodyEnd = index + 1 < matches.Count ? matches[index + 1].Index : source.Length;
                if (bodyEnd <= bodyStart)
                {
                    continue;
                }

                int length = bodyEnd - bodyStart;
                var body = new byte[length];
                Buffer.BlockCopy(source, bodyStart, body, 0, length);

                // Later revisions of an indirect object supersede earlier revisions.
                objects[objectNumber] = new PdfObject
                {
                    Number = objectNumber,
                    Body = body
                };
            }

            return objects;
        }

        private static void ExpandObjectStreams(Dictionary<int, PdfObject> objects)
        {
            foreach (PdfObject container in objects.Values.ToList())
            {
                string text = container.Text;
                if (text.IndexOf("/ObjStm", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                int count = ReadIntegerName(text, "/N");
                int first = ReadIntegerName(text, "/First");
                if (count <= 0 || count > 4096 || first <= 0)
                {
                    continue;
                }

                byte[] decoded = DecodeStream(container, MaxObjectStreamBytes);
                if (decoded == null || first > decoded.Length)
                {
                    continue;
                }

                string header = Encoding.ASCII.GetString(decoded, 0, first);
                MatchCollection pairs = Regex.Matches(header, @"(\d+)\s+(\d+)", RegexOptions.CultureInvariant);
                if (pairs.Count < count)
                {
                    continue;
                }

                var numbers = new int[count];
                var offsets = new int[count];
                bool valid = true;
                for (int index = 0; index < count; index++)
                {
                    if (!int.TryParse(pairs[index].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]) ||
                        !int.TryParse(pairs[index].Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out offsets[index]))
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                {
                    continue;
                }

                for (int index = 0; index < count; index++)
                {
                    int bodyStart = first + offsets[index];
                    int bodyEnd = index + 1 < count ? first + offsets[index + 1] : decoded.Length;
                    if (bodyStart < first || bodyEnd <= bodyStart || bodyEnd > decoded.Length)
                    {
                        continue;
                    }

                    var body = new byte[bodyEnd - bodyStart];
                    Buffer.BlockCopy(decoded, bodyStart, body, 0, body.Length);
                    objects[numbers[index]] = new PdfObject
                    {
                        Number = numbers[index],
                        Body = body
                    };
                }
            }
        }

        private static List<FileSpec> FindPdfFileSpecs(Dictionary<int, PdfObject> objects)
        {
            var specs = new List<FileSpec>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PdfObject pdfObject in objects.Values)
            {
                string text = pdfObject.Text;
                if (text.IndexOf("/EF", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                string name = FindPdfStringAfterName(text, "/UF") ?? FindPdfStringAfterName(text, "/F");
                name = CleanName(name);
                if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int streamObject = FindEmbeddedFileReference(text, objects);
                if (streamObject <= 0)
                {
                    continue;
                }

                string key = name + "\n" + streamObject.ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(key))
                {
                    continue;
                }

                specs.Add(new FileSpec
                {
                    Name = name,
                    StreamObjectNumber = streamObject
                });
            }

            specs.Sort((left, right) => NaturalStringComparer.Instance.Compare(left.Name, right.Name));
            return specs;
        }

        private static int FindEmbeddedFileReference(string fileSpecText, Dictionary<int, PdfObject> objects)
        {
            Match directDictionary = Regex.Match(
                fileSpecText,
                @"/EF\s*<<(?<body>.*?)>>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (directDictionary.Success)
            {
                int reference = FindFileReferenceInEfDictionary(directDictionary.Groups["body"].Value);
                if (reference > 0)
                {
                    return reference;
                }
            }

            Match indirectDictionary = Regex.Match(
                fileSpecText,
                @"/EF\s+(\d+)\s+\d+\s+R",
                RegexOptions.CultureInvariant);
            if (indirectDictionary.Success)
            {
                int dictionaryObject;
                if (int.TryParse(indirectDictionary.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out dictionaryObject))
                {
                    PdfObject efObject;
                    if (objects.TryGetValue(dictionaryObject, out efObject))
                    {
                        return FindFileReferenceInEfDictionary(efObject.Text);
                    }
                }
            }

            return 0;
        }

        private static int FindFileReferenceInEfDictionary(string text)
        {
            Match match = Regex.Match(
                text,
                @"/(?:UF|F)\s+(\d+)\s+\d+\s+R",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return 0;
            }

            int reference;
            return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out reference)
                ? reference
                : 0;
        }

        private static byte[] DecodeStream(PdfObject pdfObject, int maxOutputBytes)
        {
            if (pdfObject == null || pdfObject.Body == null || pdfObject.Body.Length == 0)
            {
                return null;
            }

            string text = pdfObject.Text;
            int streamKeyword = FindStreamKeyword(text);
            if (streamKeyword < 0)
            {
                return null;
            }

            int streamStart = streamKeyword + "stream".Length;
            if (streamStart < text.Length && text[streamStart] == '\r') streamStart++;
            if (streamStart < text.Length && text[streamStart] == '\n') streamStart++;

            int streamLength = ReadIntegerName(text.Substring(0, streamKeyword), "/Length");
            int streamEnd;
            if (streamLength >= 0 && streamStart + streamLength <= pdfObject.Body.Length)
            {
                streamEnd = streamStart + streamLength;
            }
            else
            {
                streamEnd = text.LastIndexOf("endstream", StringComparison.Ordinal);
                if (streamEnd < streamStart)
                {
                    return null;
                }

                while (streamEnd > streamStart && (pdfObject.Body[streamEnd - 1] == '\r' || pdfObject.Body[streamEnd - 1] == '\n'))
                {
                    streamEnd--;
                }
            }

            int compressedLength = streamEnd - streamStart;
            if (compressedLength < 0)
            {
                return null;
            }

            var streamBytes = new byte[compressedLength];
            Buffer.BlockCopy(pdfObject.Body, streamStart, streamBytes, 0, compressedLength);

            string dictionaryText = text.Substring(0, streamKeyword);
            if (dictionaryText.IndexOf("/Filter", StringComparison.Ordinal) < 0)
            {
                return streamBytes.Length <= maxOutputBytes ? streamBytes : null;
            }

            if (dictionaryText.IndexOf("/FlateDecode", StringComparison.Ordinal) >= 0 ||
                dictionaryText.IndexOf("/Fl", StringComparison.Ordinal) >= 0)
            {
                return Inflate(streamBytes, maxOutputBytes);
            }

            return null;
        }

        private static int FindStreamKeyword(string text)
        {
            int index = 0;
            while (index >= 0 && index < text.Length)
            {
                index = text.IndexOf("stream", index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                int after = index + "stream".Length;
                bool validBefore = index == 0 || char.IsWhiteSpace(text[index - 1]) || text[index - 1] == '>';
                bool validAfter = after < text.Length && (text[after] == '\r' || text[after] == '\n');
                if (validBefore && validAfter)
                {
                    return index;
                }

                index = after;
            }

            return -1;
        }

        private static byte[] Inflate(byte[] compressed, int maxOutputBytes)
        {
            byte[] output = TryInflate(compressed, 0, compressed.Length, maxOutputBytes);
            if (output != null)
            {
                return output;
            }

            // Some .NET Framework/zlib combinations expect raw DEFLATE while PDF
            // FlateDecode streams commonly carry the two-byte zlib header and
            // four-byte Adler-32 trailer. Try that form as a compatibility fallback.
            if (compressed.Length > 6)
            {
                return TryInflate(compressed, 2, compressed.Length - 6, maxOutputBytes);
            }

            return null;
        }

        private static byte[] TryInflate(byte[] compressed, int offset, int count, int maxOutputBytes)
        {
            try
            {
                using (var input = new MemoryStream(compressed, offset, count, false))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress, false))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    while (true)
                    {
                        int read = deflate.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                        {
                            break;
                        }

                        if (output.Length + read > maxOutputBytes)
                        {
                            return null;
                        }

                        output.Write(buffer, 0, read);
                    }

                    return output.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        private static int ReadIntegerName(string text, string name)
        {
            Match match = Regex.Match(
                text,
                Regex.Escape(name) + @"\s+(\d+)",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return -1;
            }

            int value;
            return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                ? value
                : -1;
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
            cleaned = cleaned.Replace('\\', '/');
            int slash = cleaned.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < cleaned.Length)
            {
                cleaned = cleaned.Substring(slash + 1);
            }

            if (cleaned.Length > 180)
            {
                cleaned = cleaned.Substring(0, 177) + "…";
            }
            return cleaned;
        }

        private static bool LooksLikePdf(byte[] data)
        {
            int limit = Math.Min(data.Length - 4, 1024);
            for (int index = 0; index <= limit; index++)
            {
                if (data[index] == (byte)'%' &&
                    data[index + 1] == (byte)'P' &&
                    data[index + 2] == (byte)'D' &&
                    data[index + 3] == (byte)'F' &&
                    data[index + 4] == (byte)'-')
                {
                    return true;
                }
            }
            return false;
        }

        private static byte[] ReadExactly(Stream stream, int requested)
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
