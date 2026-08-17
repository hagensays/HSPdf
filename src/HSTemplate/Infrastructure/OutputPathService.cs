using System;
using System.IO;

namespace HSTemplate.Infrastructure
{
    internal static class OutputPathService
    {
        public static string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string GetUniquePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("A file name is required.", nameof(fileName));
            }

            if (Path.IsPathRooted(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Only a plain file name is allowed.", nameof(fileName));
            }

            string first = Path.Combine(BaseDirectory, fileName);
            if (!File.Exists(first) && !Directory.Exists(first))
            {
                return first;
            }

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int suffix = 2; suffix < int.MaxValue; suffix++)
            {
                string candidate = Path.Combine(BaseDirectory, string.Format("{0}_{1}{2}", stem, suffix, extension));
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("Could not allocate a unique output file name.");
        }

        public static FileStream CreateUniqueFile(string fileName)
        {
            return new FileStream(GetUniquePath(fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
    }
}
