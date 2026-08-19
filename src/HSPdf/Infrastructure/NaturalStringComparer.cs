using System;
using System.Collections.Generic;

namespace HSPdf
{
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
}
