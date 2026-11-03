using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 以虛擬清單顯示圖片，並提供篩選、排序與選取通知。
    internal sealed partial class ImageBrowserControl : UserControl
    {
        // 保存完整圖片列，作為篩選與排序的來源資料。
        private readonly List<ImageListRow> source = new List<ImageListRow>();
        // 保存目前篩選及排序後的圖片列。
        private IReadOnlyList<ImageListRow> visible = new List<ImageListRow>();
        // 保存圖片檔案所在目錄，用於建立選取事件的完整路徑。
        private string imagesDirectory;
        // 記錄目前清單的排序欄位。
        private ImageListSortColumn column = ImageListSortColumn.Sequence;
        // 記錄目前清單的排序方向。
        private ImageListSortDirection direction = ImageListSortDirection.Ascending;
        // 避免重新整理清單時觸發使用者選取通知。
        private bool refreshing;

        // 通知主視窗目前清單所選圖片及其受管理檔案路徑。
        public event EventHandler<ImageSelectedEventArgs> ImageSelected;

        // 取得目前選取圖片的識別碼。
        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visible[index].Image.Id).ToArray(); } }
        // 取得尚未篩選與排序的圖片列。
        internal IReadOnlyList<ImageListRow> Rows { get { return source; } }

        /// <summary>將目前選取項目移動指定列數並確保目標列可見。</summary>
        internal void MoveSelection(int direction)
        {
            if (visible.Count == 0) return;
            int selected = list.SelectedIndices.Count == 0 ? (direction < 0 ? visible.Count : -1) : list.SelectedIndices[0];
            int target = Math.Max(0, Math.Min(visible.Count - 1, selected + direction));
            if (target == selected) return;
            list.SelectedIndices.Clear();
            list.SelectedIndices.Add(target);
            list.EnsureVisible(target);
        }

        /// <summary>依圖片識別碼選取清單中對應的圖片列。</summary>
        internal void SelectImage(string imageId)
        {
            int index = visible.Select((row, current) => new { row, current }).Where(item => item.row.Image.Id == imageId).Select(item => item.current).DefaultIfEmpty(-1).Single();
            list.SelectedIndices.Clear();
            if (index >= 0) list.SelectedIndices.Add(index);
        }

        /// <summary>清除搜尋並取代目前選取，選取仍存在的指定圖片且確保第一張可見。</summary>
        internal void SelectImages(IReadOnlyCollection<string> imageIds)
        {
            if (imageIds == null || imageIds.Count == 0) return;
            search.Clear();
            var selectedIds = new HashSet<string>(imageIds, StringComparer.Ordinal);
            var indices = visible.Select((row, index) => new { row, index })
                .Where(item => selectedIds.Contains(item.row.Image.Id))
                .Select(item => item.index)
                .ToArray();
            list.SelectedIndices.Clear();
            foreach (int index in indices) list.SelectedIndices.Add(index);
            if (indices.Length > 0) list.EnsureVisible(indices[0]);
        }

        /// <summary>初始化搜尋欄、虛擬清單及選取排序事件。</summary>
        public ImageBrowserControl()
        {
            InitializeComponent();
        }

        private void Search_TextChanged(object sender, EventArgs args) { RefreshList(); }
        private void List_SelectedIndexChanged(object sender, EventArgs args) { ShowSelectionAsync(); }
        private void List_ColumnClick(object sender, ColumnClickEventArgs args)
        {
            ChangeSort(args.Column == 0 ? ImageListSortColumn.Sequence : args.Column == 1 ? ImageListSortColumn.FileName : args.Column == 2 ? ImageListSortColumn.Classes : ImageListSortColumn.AnnotationCount);
        }

        /// <summary>將圖片資料轉成清單列並載入受管理圖片目錄。</summary>
        public void LoadImages(IReadOnlyList<ProjectImage> images, string managedImagesDirectory)
        {
            LoadRows((images ?? new ProjectImage[0]).Select(image => new ImageListRow(image, new string[0], 0, false)), managedImagesDirectory);
        }

        /// <summary>載入完整圖片列並重設可見欄位與搜尋結果。</summary>
        public void LoadRows(IEnumerable<ImageListRow> rows, string managedImagesDirectory)
        {
            source.Clear();
            if (rows != null) source.AddRange(rows);
            imagesDirectory = managedImagesDirectory;
            bool showAnnotationCount = source.Any(row => row.ShowAnnotationCount);
            if (showAnnotationCount && !list.Columns.Contains(annotationCountColumn)) list.Columns.Add(annotationCountColumn);
            if (!showAnnotationCount && list.Columns.Contains(annotationCountColumn)) list.Columns.Remove(annotationCountColumn);
            refreshing = true;
            try { RefreshList(); }
            finally { refreshing = false; }
        }

        /// <summary>切換指定排序欄位的升降冪方向並更新清單。</summary>
        private void ChangeSort(ImageListSortColumn requestedColumn)
        {
            if (column == requestedColumn) direction = direction == ImageListSortDirection.Ascending ? ImageListSortDirection.Descending : ImageListSortDirection.Ascending;
            else
            {
                column = requestedColumn;
                direction = ImageListSortDirection.Ascending;
            }
            RefreshList();
        }

        /// <summary>套用搜尋與排序，保留仍可見圖片的既有選取。</summary>
        private void RefreshList()
        {
            var selectedIds = new HashSet<string>(list.SelectedIndices.Cast<int>().Select(index => visible[index].Image.Id), StringComparer.Ordinal);
            visible = ImageListProjection.FilterAndSort(source, search.Text, column, direction);
            list.VirtualListSize = visible.Count;
            list.SelectedIndices.Clear();
            foreach (int selectedIndex in visible.Select((row, index) => new { row, index }).Where(item => selectedIds.Contains(item.row.Image.Id)).Select(item => item.index))
                list.SelectedIndices.Add(selectedIndex);
            list.Invalidate();
        }

        /// <summary>依可見列索引建立虛擬清單項目及其欄位文字。</summary>
        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs args)
        {
            ImageListRow row = visible[args.ItemIndex];
            args.Item = new ListViewItem(row.Image.Sequence.ToString());
            args.Item.SubItems.Add(row.Image.OriginalFileName);
            args.Item.SubItems.Add(row.ClassDisplay);
            if (list.Columns.Contains(annotationCountColumn)) args.Item.SubItems.Add(row.AnnotationCount.ToString());
        }

        /// <summary>通知主視窗第一個選取圖片及其受管理檔案路徑。</summary>
        private void ShowSelectionAsync()
        {
            if (refreshing || list.SelectedIndices.Count == 0) return;
            ProjectImage image = visible[list.SelectedIndices[0]].Image;
            ImageSelected?.Invoke(this, new ImageSelectedEventArgs(image, Path.Combine(imagesDirectory, image.ManagedFileName)));
        }
    }

    // 傳遞清單選取圖片及其受管理檔案路徑。
    internal sealed class ImageSelectedEventArgs : EventArgs
    {
        /// <summary>建立圖片選取事件資料。</summary>
        public ImageSelectedEventArgs(ProjectImage image, string path)
        {
            Image = image;
            Path = path;
        }

        // 取得被選取的圖片資料。
        public ProjectImage Image { get; private set; }
        // 取得圖片受管理檔案的完整路徑。
        public string Path { get; private set; }
    }
}
