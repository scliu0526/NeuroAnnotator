using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private IReadOnlyList<string> contextImageIds = new string[0];
        private readonly ContextMenuStrip imageMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem clearRetestItem = new ToolStripMenuItem(Strings.DatasetClearRetest);
        private readonly ToolStripMenuItem clearClassItem = new ToolStripMenuItem(Strings.DatasetClearClass);
        private readonly ToolStripMenuItem clearSetItem = new ToolStripMenuItem(Strings.DatasetClearSet);

        // 通知主視窗目前清單所選圖片及其受管理檔案路徑。
        public event EventHandler<ImageSelectedEventArgs> ImageSelected;
        public event Action<IReadOnlyList<string>> ClearRetestRequested;
        public event Action<IReadOnlyList<string>> ClearClassRequested;
        // 通知主視窗要求清除所選圖片的資料集狀態。
        public event Action<IReadOnlyList<string>> ClearDatasetRequested;

        internal bool RetestContextMenuEnabled
        {
            get { return list.ContextMenuStrip != null; }
            set { list.ContextMenuStrip = value ? imageMenu : null; }
        }

        // 取得目前選取圖片的識別碼。
        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visible[index].Image.Id).ToArray(); } }
        // 取得尚未篩選與排序的圖片列。
        internal IReadOnlyList<ImageListRow> Rows { get { return source; } }

        /// <summary>
        /// 將目前選取項目移動指定列數並確保目標列可見。
        /// </summary>
        /// <param name="direction">選取或移動的方向。</param>
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

        /// <summary>
        /// 依圖片識別碼選取清單中對應的圖片列。
        /// </summary>
        /// <param name="imageId">圖片的識別碼。</param>
        internal void SelectImage(string imageId)
        {
            int index = visible.Select((row, current) => new { row, current }).Where(item => item.row.Image.Id == imageId).Select(item => item.current).DefaultIfEmpty(-1).Single();
            list.SelectedIndices.Clear();
            if (index >= 0) list.SelectedIndices.Add(index);
        }

        /// <summary>
        /// 清除搜尋並取代目前選取，選取仍存在的指定圖片且確保第一張可見。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
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

        /// <summary>
        /// 初始化搜尋欄、虛擬清單及選取排序事件。
        /// </summary>
        public ImageBrowserControl()
        {
            InitializeComponent();
            imageMenu.Items.Add(clearRetestItem);
            imageMenu.Items.Add(clearClassItem);
            imageMenu.Items.Add(clearSetItem);
            imageMenu.Opening += ImageMenu_Opening;
            clearRetestItem.Click += ClearRetestItem_Click;
            clearClassItem.Click += (sender, args) => { if (contextImageIds.Count > 0) ClearClassRequested?.Invoke(contextImageIds); };
            clearSetItem.Click += (sender, args) => { if (contextImageIds.Count > 0) ClearDatasetRequested?.Invoke(contextImageIds); };
        }

        private void ImageMenu_Opening(object sender, System.ComponentModel.CancelEventArgs args)
        {
            Point location = list.PointToClient(Cursor.Position);
            ListViewItem item = list.HitTest(location).Item;
            if (item == null || !item.Selected)
            {
                args.Cancel = true;
                contextImageIds = new string[0];
                return;
            }
            contextImageIds = SelectedImageIds.ToArray();
            clearRetestItem.Text = string.Format("{0} ({1})", Strings.DatasetClearRetest, contextImageIds.Count);
            clearClassItem.Text = string.Format("{0} ({1})", Strings.DatasetClearClass, contextImageIds.Count);
            clearSetItem.Text = string.Format("{0} ({1})", Strings.DatasetClearSet, contextImageIds.Count);
        }

        private void ClearRetestItem_Click(object sender, EventArgs args)
        {
            if (contextImageIds.Count > 0) ClearRetestRequested?.Invoke(contextImageIds);
        }

        /// <summary>
        /// 依搜尋欄內容重新篩選圖片清單。
        /// </summary>
        /// <param name="sender">內容已變更的搜尋欄。</param>
        /// <param name="args">文字變更事件資料。</param>
        private void Search_TextChanged(object sender, EventArgs args) { RefreshList(); }

        /// <summary>
        /// 將目前圖片清單選取狀態通知外層控制項。
        /// </summary>
        /// <param name="sender">選取項目已變更的圖片清單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void List_SelectedIndexChanged(object sender, EventArgs args) { ShowSelectionAsync(); }

        /// <summary>
        /// 依點擊欄位切換圖片清單的排序欄位與方向。
        /// </summary>
        /// <param name="sender">欄位標題被點擊的圖片清單。</param>
        /// <param name="args">指出被點擊欄位索引的事件資料。</param>
        private void List_ColumnClick(object sender, ColumnClickEventArgs args)
        {
            ChangeSort(args.Column == 0 ? ImageListSortColumn.Sequence : args.Column == 1 ? ImageListSortColumn.FileName : args.Column == 2 ? ImageListSortColumn.Classes : ImageListSortColumn.AnnotationCount);
        }

        /// <summary>
        /// 將圖片資料轉成清單列並載入受管理圖片目錄。
        /// </summary>
        /// <param name="images">要載入或處理的圖片資料。</param>
        /// <param name="managedImagesDirectory">圖片實際檔案所在的受管理目錄。</param>
        public void LoadImages(IReadOnlyList<ProjectImage> images, string managedImagesDirectory)
        {
            LoadRows((images ?? new ProjectImage[0]).Select(image => new ImageListRow(image, new string[0], 0, false, false)), managedImagesDirectory);
        }

        /// <summary>
        /// 載入完整圖片列並重設可見欄位與搜尋結果。
        /// </summary>
        /// <param name="rows">要載入或篩選的資料列。</param>
        /// <param name="managedImagesDirectory">圖片實際檔案所在的受管理目錄。</param>
        public void LoadRows(IEnumerable<ImageListRow> rows, string managedImagesDirectory)
        {
            source.Clear();
            if (rows != null) source.AddRange(rows);
            imagesDirectory = managedImagesDirectory;
            bool showClass = source.Any(row => row.ShowClass);
            bool showAnnotationCount = source.Any(row => row.ShowAnnotationCount);
            if ((!showClass && column == ImageListSortColumn.Classes) || (!showAnnotationCount && column == ImageListSortColumn.AnnotationCount))
            {
                column = ImageListSortColumn.Sequence;
                direction = ImageListSortDirection.Ascending;
            }
            if (showClass && !list.Columns.Contains(classColumn)) list.Columns.Insert(2, classColumn);
            if (!showClass && list.Columns.Contains(classColumn)) list.Columns.Remove(classColumn);
            if (showAnnotationCount && !list.Columns.Contains(annotationCountColumn)) list.Columns.Add(annotationCountColumn);
            if (!showAnnotationCount && list.Columns.Contains(annotationCountColumn)) list.Columns.Remove(annotationCountColumn);
            refreshing = true;
            try { RefreshList(); }
            finally { refreshing = false; }
        }

        /// <summary>
        /// 切換指定排序欄位的升降冪方向並更新清單。
        /// </summary>
        /// <param name="requestedColumn">要求排序的欄位。</param>
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

        /// <summary>
        /// 套用搜尋與排序，保留仍可見圖片的既有選取。
        /// </summary>
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

        /// <summary>
        /// 依可見列索引建立虛擬清單項目及其欄位文字。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs args)
        {
            ImageListRow row = visible[args.ItemIndex];
            args.Item = new ListViewItem(row.Image.Sequence.ToString());
            args.Item.SubItems.Add(row.Image.OriginalFileName);
            if (list.Columns.Contains(classColumn)) args.Item.SubItems.Add(row.ClassDisplay);
            if (list.Columns.Contains(annotationCountColumn)) args.Item.SubItems.Add(row.AnnotationCount.ToString());
        }

        /// <summary>
        /// 通知主視窗第一個選取圖片及其受管理檔案路徑。
        /// </summary>
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
        /// <summary>
        /// 建立圖片選取事件資料。
        /// </summary>
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

    internal class SelectionPreservingListView : ListView
    {
        private const int RightButtonDownMessage = 0x0204;

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == RightButtonDownMessage)
            {
                long coordinates = message.LParam.ToInt64();
                var location = new Point((short)(coordinates & 0xffff), (short)((coordinates >> 16) & 0xffff));
                ListViewItem item = HitTest(location).Item;
                if (item == null || !item.Selected)
                {
                    message.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class HorizontalWheelListView : SelectionPreservingListView
    {
        private const int MouseWheelMessage = 0x020A;
        private const int ShiftKey = 0x0004;
        private const int ControlKey = 0x0008;
        private const int HorizontalScrollBar = 0;
        private const int ListViewScrollMessage = 0x1014;
        private const int ScrollRangeAndPage = 0x0003;
        private const int WheelDelta = 120;
        private const int PixelsPerWheelNotch = 48;
        private int horizontalWheelDelta;

        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollInfo
        {
            public uint Size;
            public uint Mask;
            public int Minimum;
            public int Maximum;
            public uint Page;
            public int Position;
            public int TrackPosition;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetScrollInfo(IntPtr handle, int bar, ref ScrollInfo info);

        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
        private static extern IntPtr SendListViewMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == MouseWheelMessage)
            {
                long keyState = message.WParam.ToInt64() & 0xffff;
                if ((keyState & ShiftKey) != 0 && (keyState & ControlKey) == 0)
                {
                    ScrollHorizontally((short)((message.WParam.ToInt64() >> 16) & 0xffff));
                    message.Result = IntPtr.Zero;
                    return;
                }
                horizontalWheelDelta = 0;
            }
            base.WndProc(ref message);
        }

        private void ScrollHorizontally(int delta)
        {
            if (delta == 0 || !HasHorizontalScrollRange())
            {
                horizontalWheelDelta = 0;
                return;
            }
            horizontalWheelDelta += delta;
            int notches = horizontalWheelDelta / WheelDelta;
            horizontalWheelDelta -= notches * WheelDelta;
            if (notches != 0) SendListViewMessage(Handle, ListViewScrollMessage, new IntPtr(-notches * PixelsPerWheelNotch), IntPtr.Zero);
        }

        private bool HasHorizontalScrollRange()
        {
            var info = new ScrollInfo { Size = (uint)Marshal.SizeOf(typeof(ScrollInfo)), Mask = ScrollRangeAndPage };
            if (!GetScrollInfo(Handle, HorizontalScrollBar, ref info)) return false;
            long maximumPosition = (long)info.Maximum - Math.Max(0L, (long)info.Page - 1L);
            return maximumPosition > info.Minimum;
        }
    }
}
