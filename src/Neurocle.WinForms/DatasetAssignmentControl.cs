using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 顯示並編輯圖片在目前標註集中的資料集分配狀態。
    internal sealed partial class DatasetAssignmentControl : UserControl
    {
        // 防止更新訓練比例時再次觸發互相同步事件。
        private bool synchronizingPercentages;
        // 保存目前載入的所有資料集列。
        private readonly List<DatasetRow> rows = new List<DatasetRow>();
        // 保存套用篩選條件後的可見資料集列。
        private readonly List<DatasetRow> visibleRows = new List<DatasetRow>();
        private IReadOnlyList<string> contextImageIds = new string[0];

        /// <summary>
        /// 初始化資料集控制項並接線執行期操作事件。
        /// </summary>
        public DatasetAssignmentControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 同步訓練比例變更後的測試比例。
        /// </summary>
        /// <param name="sender">引發事件的比例欄位。</param>
        /// <param name="args">數值變更事件資料。</param>
        private void TrainPercent_ValueChanged(object sender, EventArgs args) { SynchronizePercentages(trainPercent, testPercent); }

        /// <summary>
        /// 同步測試比例變更後的訓練比例。
        /// </summary>
        /// <param name="sender">引發事件的比例欄位。</param>
        /// <param name="args">數值變更事件資料。</param>
        private void TestPercent_ValueChanged(object sender, EventArgs args) { SynchronizePercentages(testPercent, trainPercent); }

        /// <summary>
        /// 套用資料集、類別、標註或 Retest 篩選變更。
        /// </summary>
        /// <param name="sender">引發事件的篩選欄位。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void Filter_SelectedIndexChanged(object sender, EventArgs args) { RefreshRows(); }

        /// <summary>
        /// 提供虛擬清單目前索引所需的圖片列。
        /// </summary>
        /// <param name="sender">提出資料要求的虛擬清單。</param>
        /// <param name="args">包含列索引並接收建立項目的事件資料。</param>
        private void List_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs args) { args.Item = CreateItem(visibleRows[args.ItemIndex]); }

        /// <summary>
        /// 依目前選取項目更新切分所選按鈕狀態。
        /// </summary>
        /// <param name="sender">選取項目變更的清單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void List_SelectedIndexChanged(object sender, EventArgs args) { UpdateSplitButtons(); }

        /// <summary>
        /// 要求將所選圖片分配至訓練集。
        /// </summary>
        /// <param name="sender">引發事件的訓練集按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void TrainButton_Click(object sender, EventArgs args) { DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Train); }

        /// <summary>
        /// 要求將所選圖片分配至測試集。
        /// </summary>
        /// <param name="sender">引發事件的測試集按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void TestButton_Click(object sender, EventArgs args) { DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Test); }

        /// <summary>
        /// 要求將所選圖片設為未分配。
        /// </summary>
        /// <param name="sender">引發事件的未分配按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void UnassignedButton_Click(object sender, EventArgs args) { DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Unassigned); }

        /// <summary>
        /// 要求將所選圖片設為不使用。
        /// </summary>
        /// <param name="sender">引發事件的不使用按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void NotUsedButton_Click(object sender, EventArgs args) { DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.NotUsed); }

        /// <summary>
        /// 要求為所選圖片設定 Retest 旗標。
        /// </summary>
        /// <param name="sender">引發事件的 Retest 按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void RetestButton_Click(object sender, EventArgs args) { RetestRequested?.Invoke(SelectedImageIds, true); }

        /// <summary>
        /// 確認後依目前比例切分所選且仍可用的圖片。
        /// </summary>
        /// <param name="sender">引發事件的切分所選按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SplitSelectedButton_Click(object sender, EventArgs args)
        {
            int count = SelectedImageIds.Count(id => rows.Any(row => row.Image.Id == id && !row.Image.IsMissing));
            if (count > 0 && MessageBox.Show(string.Format(Strings.DatasetSplitSelectedConfirm, count), Strings.DatasetSplitSelected, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                AutoSplitSelectedRequested?.Invoke(SelectedImageIds, (int)trainPercent.Value);
        }

        /// <summary>
        /// 確認後依目前比例切分全部仍可用的圖片。
        /// </summary>
        /// <param name="sender">引發事件的切分全部按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SplitAllButton_Click(object sender, EventArgs args)
        {
            int count = rows.Count(row => !row.Image.IsMissing);
            if (MessageBox.Show(string.Format(Strings.DatasetSplitAllConfirm, count), Strings.DatasetSplitAll, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                AutoSplitAllRequested?.Invoke((int)trainPercent.Value);
        }

        /// <summary>
        /// 要求清除所選圖片的 Retest 旗標。
        /// </summary>
        /// <param name="sender">引發事件的右鍵選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void ClearRetestItem_Click(object sender, EventArgs args) { if (contextImageIds.Count > 0) ClearRetestRequested?.Invoke(contextImageIds); }

        private void DatasetMenu_Opening(object sender, System.ComponentModel.CancelEventArgs args)
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
        }

        /// <summary>
        /// 要求清除所選圖片的 CLA 類別。
        /// </summary>
        /// <param name="sender">引發事件的右鍵選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void ClearClassItem_Click(object sender, EventArgs args) { ClearClassRequested?.Invoke(SelectedImageIds); }

        /// <summary>
        /// 要求將所選圖片的資料集狀態設為未分配。
        /// </summary>
        /// <param name="sender">引發事件的右鍵選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void ClearSetItem_Click(object sender, EventArgs args) { DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Unassigned); }

        // 通知主視窗更新所選圖片的資料集狀態。
        public event Action<IReadOnlyList<string>, DatasetAssignmentStatus> DatasetRequested;
        // 通知主視窗更新所選圖片的 Retest 旗標。
        public event Action<IReadOnlyList<string>, bool> RetestRequested;
        public event Action<IReadOnlyList<string>> ClearRetestRequested;
        // 通知主視窗清除所選圖片的 CLA 類別。
        public event Action<IReadOnlyList<string>> ClearClassRequested;
        // 通知主視窗以目前比例自動切分資料集。
        public event Action<IReadOnlyList<string>, int> AutoSplitSelectedRequested;
        // 通知主視窗依目前比例切分所有可用圖片。
        public event Action<int> AutoSplitAllRequested;

        // 取得目前已載入的全部資料集列。
        internal IReadOnlyList<DatasetRow> Rows { get { return rows; } }

        /// <summary>
        /// 同步訓練與測試比例，使兩者總和維持百分之百。
        /// </summary>
        /// <param name="source">提供資料或數值的來源。</param>
        /// <param name="target">要更新的目標控制項或值。</param>
        private void SynchronizePercentages(NumericUpDown source, NumericUpDown target)
        {
            if (synchronizingPercentages) return;
            synchronizingPercentages = true;
            target.Value = 100 - source.Value;
            synchronizingPercentages = false;
        }

        // 取得目前清單選取的圖片識別碼。
        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visibleRows[index].Image.Id).ToArray(); } }

        /// <summary>
        /// 載入資料集列並更新類別篩選及可選的標註數欄位。
        /// </summary>
        /// <param name="values">要載入的值集合。</param>
        /// <param name="showAnnotationCount">是否顯示標註數量欄位。</param>
        public void LoadRows(IEnumerable<DatasetRow> values, bool showAnnotationCount = false)
        {
            if (showAnnotationCount && !list.Columns.Contains(annotationCountColumn)) list.Columns.Insert(5, annotationCountColumn);
            if (!showAnnotationCount && list.Columns.Contains(annotationCountColumn)) list.Columns.Remove(annotationCountColumn);
            rows.Clear();
            rows.AddRange(values ?? Enumerable.Empty<DatasetRow>());
            string selectedClass = classFilter.SelectedItem as string;
            classFilter.Items.Clear();
            classFilter.Items.Add(Strings.DatasetFilterAll);
            foreach (string value in rows.SelectMany(row => row.ClassNames).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)) classFilter.Items.Add(value);
            classFilter.SelectedItem = classFilter.Items.Contains(selectedClass) ? selectedClass : Strings.DatasetFilterAll;
            RefreshRows();
        }

        /// <summary>
        /// 依資料集、類別、標註及 Retest 篩選條件重建可見列。
        /// </summary>
        private void RefreshRows()
        {
            if (list == null) return;
            var selectedIds = new HashSet<string>(list.SelectedIndices.Cast<int>().Where(index => index >= 0 && index < visibleRows.Count).Select(index => visibleRows[index].Image.Id), StringComparer.Ordinal);
            string set = setFilter.SelectedItem as string;
            string className = classFilter.SelectedItem as string;
            string annotation = annotationFilter.SelectedItem as string;
            string retest = retestFilter.SelectedItem as string;
            IEnumerable<DatasetRow> filtered = rows.Where(row =>
                (set == Strings.DatasetFilterAll || DatasetText(row.Assignment.Status) == set) &&
                (className == Strings.DatasetFilterAll || row.ClassNames.Any(value => string.Equals(value, className, StringComparison.Ordinal))) &&
                (annotation == Strings.DatasetFilterAll || (annotation == Strings.DatasetFilterAnnotated) == row.HasAnnotation) &&
                (retest == Strings.DatasetFilterAll || (retest == Strings.DatasetFilterRetest) == row.Assignment.Retest));
            visibleRows.Clear();
            visibleRows.AddRange(filtered);
            list.VirtualListSize = visibleRows.Count;
            list.SelectedIndices.Clear();
            foreach (int index in visibleRows.Select((row, index) => new { row, index }).Where(item => selectedIds.Contains(item.row.Image.Id)).Select(item => item.index))
                list.SelectedIndices.Add(index);
            list.Invalidate();
            UpdateSplitButtons();
        }

        /// <summary>
        /// 依目前清單選取狀態更新切分所選按鈕。
        /// </summary>
        private void UpdateSplitButtons()
        {
            if (splitSelectedButton != null) splitSelectedButton.Enabled = list.SelectedIndices.Count > 0;
        }

        /// <summary>
        /// 將資料集列轉換為虛擬清單使用的顯示項目。
        /// </summary>
        /// <param name="row">要轉換成清單項目的資料集資料列。</param>
        /// <returns>建立的圖片或資料集清單項目。</returns>
        private ListViewItem CreateItem(DatasetRow row)
        {
            var item = new ListViewItem(row.Image.Sequence.ToString()) { Tag = row.Image.Id };
            item.SubItems.Add(row.Image.OriginalFileName);
            item.SubItems.Add(DatasetText(row.Assignment.Status));
            item.SubItems.Add(string.Join(", ", row.ClassNames));
            item.SubItems.Add(row.HasAnnotation ? Strings.DatasetFilterAnnotated : Strings.DatasetFilterEmpty);
            if (list.Columns.Contains(annotationCountColumn)) item.SubItems.Add(row.AnnotationCount.ToString());
            item.SubItems.Add(row.Assignment.Retest ? Strings.DatasetFilterRetest : Strings.DatasetFilterNoRetest);
            return item;
        }

        /// <summary>
        /// 將資料集分配狀態轉成畫面顯示文字。
        /// </summary>
        /// <param name="status">要處理或顯示的狀態。</param>
        /// <returns>轉換或取得的文字結果。</returns>
        private static string DatasetText(DatasetAssignmentStatus status)
        {
            if (status == DatasetAssignmentStatus.Train) return Strings.DatasetTrain;
            if (status == DatasetAssignmentStatus.Test) return Strings.DatasetTest;
            if (status == DatasetAssignmentStatus.NotUsed) return Strings.DatasetNotUsed;
            return Strings.DatasetUnassigned;
        }
    }

    // 將圖片、資料集狀態及標註摘要合併為一列畫面資料。
    internal sealed class DatasetRow
    {
        /// <summary>
        /// 建立包含圖片、分配、類別及標註數量的資料列。
        /// </summary>
        public DatasetRow(ProjectImage image, DatasetAssignment assignment, IReadOnlyList<string> classNames, int annotationCount)
        {
            Image = image; Assignment = assignment; ClassNames = classNames ?? throw new ArgumentNullException(nameof(classNames)); AnnotationCount = annotationCount;
        }
        // 取得資料列代表的圖片。
        public ProjectImage Image { get; private set; }
        // 取得圖片在目前標註集中的分配狀態。
        public DatasetAssignment Assignment { get; private set; }
        // 取得圖片上實際使用的類別名稱。
        public IReadOnlyList<string> ClassNames { get; private set; }
        // 取得圖片目前的標註數量。
        public int AnnotationCount { get; private set; }
        // 指出圖片是否至少包含一筆標註。
        public bool HasAnnotation { get { return AnnotationCount > 0; } }
    }
}
