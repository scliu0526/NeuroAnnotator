using System;
using System.Collections.Generic;
using System.Linq;

namespace Neurocle.Application.Projects
{
    // 圖片清單可用的排序欄位。
    public enum ImageListSortColumn
    {
        // 依圖片顯示序號排序。
        Sequence,
        // 依原始檔名的自然排序排序。
        FileName,
        // 依標註類別名稱排序。
        Classes,
        // 依標註數量排序。
        AnnotationCount,
        // 依資料集狀態排序。
        Dataset,
        // 依 Retest 狀態排序。
        Retest
    }

    // 圖片清單的排序方向。
    public enum ImageListSortDirection
    {
        // 由小至大或由 A 至 Z 排序。
        Ascending,
        // 由大至小或由 Z 至 A 排序。
        Descending
    }

    // 將圖片資料依搜尋字串與清單欄位轉換為排序後的顯示集合。
    public static class ImageListProjection
    {
        /// <summary>
        /// 篩選圖片並排序；僅 Sequence 使用序號，其餘欄位以原始檔名自然排序；檔名比對不區分大小寫。
        /// </summary>
        /// <param name="images">待顯示的圖片集合。</param>
        /// <param name="searchText">要在原始檔名中搜尋的文字；空白值不篩選。</param>
        /// <param name="column">選取的排序欄位。</param>
        /// <param name="direction">排序方向。</param>
        /// <returns>依指定條件排序後的新清單。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="images"/> 為 null。</exception>
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

        /// <summary>
        /// 篩選並排序包含類別與標註摘要的圖片列。
        /// </summary>
        /// <param name="rows">待顯示的圖片列集合。</param>
        /// <param name="searchText">要在原始檔名中搜尋的文字；空白值不篩選。</param>
        /// <param name="column">選取的排序欄位。</param>
        /// <param name="direction">排序方向。</param>
        /// <returns>依指定條件排序後的新圖片列清單。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rows"/> 為 null。</exception>
        public static IReadOnlyList<ImageListRow> FilterAndSort(IEnumerable<ImageListRow> rows, string searchText, ImageListSortColumn column, ImageListSortDirection direction)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            IEnumerable<ImageListRow> result = rows;
            if (!string.IsNullOrWhiteSpace(searchText)) result = result.Where(row => row.Image.OriginalFileName.IndexOf(searchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            if (column == ImageListSortColumn.Classes)
            {
                IOrderedEnumerable<ImageListRow> ordered = result.OrderBy(row => row.ClassNames.Count == 0);
                return (direction == ImageListSortDirection.Ascending
                    ? ordered.ThenBy(row => row.ClassDisplay, StringComparer.Ordinal)
                    : ordered.ThenByDescending(row => row.ClassDisplay, StringComparer.Ordinal)).ToList();
            }
            if (column == ImageListSortColumn.AnnotationCount)
            {
                return (direction == ImageListSortDirection.Ascending
                    ? result.OrderBy(row => row.AnnotationCount)
                    : result.OrderByDescending(row => row.AnnotationCount)).ToList();
            }
            if (column == ImageListSortColumn.Dataset)
            {
                return (direction == ImageListSortDirection.Ascending
                    ? result.OrderBy(row => row.DatasetStatus)
                    : result.OrderByDescending(row => row.DatasetStatus)).ToList();
            }
            if (column == ImageListSortColumn.Retest)
            {
                return (direction == ImageListSortDirection.Ascending
                    ? result.OrderBy(row => row.Retest)
                    : result.OrderByDescending(row => row.Retest)).ToList();
            }
            IOrderedEnumerable<ImageListRow> imageOrdered = column == ImageListSortColumn.Sequence
                ? result.OrderBy(row => row.Image.Sequence).ThenBy(row => row.Image.OriginalFileName, NaturalFileNameComparer.Instance)
                : result.OrderBy(row => row.Image.OriginalFileName, NaturalFileNameComparer.Instance).ThenBy(row => row.Image.Sequence);
            return (direction == ImageListSortDirection.Ascending ? imageOrdered : imageOrdered.Reverse()).ToList();
        }

        // 比較檔名文字與數字片段，使 image2 排在 image10 之前。
        private sealed class NaturalFileNameComparer : IComparer<string>
        {
            // 共用的自然檔名比較器實例。
            public static readonly NaturalFileNameComparer Instance = new NaturalFileNameComparer();

            /// <summary>
            /// 以不區分字母大小寫的字元順序及數字值比較兩個檔名。
            /// </summary>
            /// <param name="left">排序中的第一個檔名。</param>
            /// <param name="right">排序中的第二個檔名。</param>
            /// <returns>依排序先後回傳負數、零或正數。</returns>
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

    // 供圖片清單呈現的圖片、類別及標註數量摘要。
    public sealed class ImageListRow
    {
        /// <summary>
        /// 建立圖片清單顯示列，並將類別名稱去重後排序。
        /// </summary>
        /// <param name="image">對應的圖片資料。</param>
        /// <param name="classNames">圖片上已使用的類別名稱。</param>
        /// <param name="annotationCount">圖片目前的標註數量。</param>
        /// <param name="showAnnotationCount">介面是否顯示標註數量。</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> 或 <paramref name="classNames"/> 為 null。</exception>
        public ImageListRow(ProjectImage image, IReadOnlyList<string> classNames, int annotationCount, bool showAnnotationCount)
            : this(image, classNames, annotationCount, true, showAnnotationCount) { }

        /// <summary>
        /// 建立指定欄位可見性的圖片清單顯示列。
        /// </summary>
        public ImageListRow(ProjectImage image, IReadOnlyList<string> classNames, int annotationCount, bool showClass, bool showAnnotationCount)
            : this(image, classNames, annotationCount, showClass, showAnnotationCount, null, null, false, false) { }

        /// <summary>
        /// 建立包含資料集與 Retest 摘要的圖片清單顯示列。
        /// </summary>
        public ImageListRow(ProjectImage image, IReadOnlyList<string> classNames, int annotationCount, bool showClass, bool showAnnotationCount,
            DatasetAssignmentStatus? datasetStatus, bool? retest, bool showDataset, bool showRetest)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            ClassNames = (classNames ?? throw new ArgumentNullException(nameof(classNames))).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            AnnotationCount = annotationCount;
            ShowClass = showClass;
            ShowAnnotationCount = showAnnotationCount;
            DatasetStatus = datasetStatus;
            Retest = retest;
            ShowDataset = showDataset;
            ShowRetest = showRetest;
        }

        // 取得此顯示列所代表的圖片。
        public ProjectImage Image { get; private set; }
        // 取得去重並依字串順序排列的類別名稱。
        public IReadOnlyList<string> ClassNames { get; private set; }
        // 取得此圖片目前的標註數量。
        public int AnnotationCount { get; private set; }
        // 取得清單是否應顯示類別欄位。
        public bool ShowClass { get; private set; }
        // 取得清單是否應顯示標註數量。
        public bool ShowAnnotationCount { get; private set; }
        // 取得目前資料集狀態；沒有標註集時為 null。
        public DatasetAssignmentStatus? DatasetStatus { get; private set; }
        // 取得目前 Retest 狀態；沒有標註集時為 null。
        public bool? Retest { get; private set; }
        // 取得清單是否應顯示資料集欄位。
        public bool ShowDataset { get; private set; }
        // 取得清單是否應顯示 Retest 欄位。
        public bool ShowRetest { get; private set; }
        // 取得以逗號分隔的類別名稱顯示文字。
        public string ClassDisplay { get { return string.Join(", ", ClassNames); } }
    }
}
