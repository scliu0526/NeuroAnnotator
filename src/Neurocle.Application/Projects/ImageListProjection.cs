using System;
using System.Collections.Generic;
using System.Linq;

namespace Neurocle.Application.Projects
{
    public enum ImageListSortColumn
    {
        Sequence,
        FileName
    }

    public enum ImageListSortDirection
    {
        Ascending,
        Descending
    }

    public static class ImageListProjection
    {
        public static IReadOnlyList<ProjectImage> FilterAndSort(IEnumerable<ProjectImage> images, string searchText, ImageListSortColumn column, ImageListSortDirection direction)
        {
            if (images == null) throw new ArgumentNullException(nameof(images));
            IEnumerable<ProjectImage> result = images;
            if (!string.IsNullOrWhiteSpace(searchText)) result = result.Where(image => image.OriginalFileName.IndexOf(searchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            IOrderedEnumerable<ProjectImage> ordered = column == ImageListSortColumn.Sequence
                ? result.OrderBy(image => image.Sequence).ThenBy(image => image.OriginalFileName, NaturalFileNameComparer.Instance)
                : result.OrderBy(image => image.OriginalFileName, NaturalFileNameComparer.Instance).ThenBy(image => image.Sequence);
            return (direction == ImageListSortDirection.Ascending ? ordered : ordered.Reverse()).ToList();
        }

        private sealed class NaturalFileNameComparer : IComparer<string>
        {
            public static readonly NaturalFileNameComparer Instance = new NaturalFileNameComparer();

            public int Compare(string left, string right)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left == null) return -1;
                if (right == null) return 1;
                int leftIndex = 0;
                int rightIndex = 0;
                while (leftIndex < left.Length && rightIndex < right.Length)
                {
                    char leftChar = left[leftIndex];
                    char rightChar = right[rightIndex];
                    if (char.IsDigit(leftChar) && char.IsDigit(rightChar))
                    {
                        int leftEnd = leftIndex;
                        int rightEnd = rightIndex;
                        while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                        while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;
                        string leftNumber = left.Substring(leftIndex, leftEnd - leftIndex).TrimStart('0');
                        string rightNumber = right.Substring(rightIndex, rightEnd - rightIndex).TrimStart('0');
                        int numberComparison = leftNumber.Length.CompareTo(rightNumber.Length);
                        if (numberComparison == 0) numberComparison = string.Compare(leftNumber, rightNumber, StringComparison.Ordinal);
                        if (numberComparison != 0) return numberComparison;
                        leftIndex = leftEnd;
                        rightIndex = rightEnd;
                        continue;
                    }
                    int comparison = char.ToUpperInvariant(leftChar).CompareTo(char.ToUpperInvariant(rightChar));
                    if (comparison != 0) return comparison;
                    leftIndex++;
                    rightIndex++;
                }
                return left.Length.CompareTo(right.Length);
            }
        }
    }
}
