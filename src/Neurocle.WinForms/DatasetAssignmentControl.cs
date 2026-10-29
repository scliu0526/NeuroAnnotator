using System;
using System.Collections.Generic;
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

        /// <summary>初始化資料集控制項並接線執行期操作事件。</summary>
        public DatasetAssignmentControl()
        {
            InitializeComponent();
            WireRuntimeEvents();
        }

        /// <summary>接線篩選、分配、Retest 及自動切分的使用者操作。</summary>
        private void WireRuntimeEvents()
        {
            trainPercent.ValueChanged += (sender, args) => SynchronizePercentages(trainPercent, testPercent);
            testPercent.ValueChanged += (sender, args) => SynchronizePercentages(testPercent, trainPercent);
            setFilter.SelectedIndexChanged += (sender, args) => RefreshRows();
            classFilter.SelectedIndexChanged += (sender, args) => RefreshRows();
            annotationFilter.SelectedIndexChanged += (sender, args) => RefreshRows();
            retestFilter.SelectedIndexChanged += (sender, args) => RefreshRows();
            list.RetrieveVirtualItem += (sender, args) => args.Item = CreateItem(visibleRows[args.ItemIndex]);
            trainButton.Click += (sender, args) => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Train);
            testButton.Click += (sender, args) => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Test);
            unassignedButton.Click += (sender, args) => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Unassigned);
            notUsedButton.Click += (sender, args) => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.NotUsed);
            retestButton.Click += (sender, args) => RetestRequested?.Invoke(SelectedImageIds, true);
            autoSplitButton.Click += (sender, args) =>
            {
                if (MessageBox.Show(Strings.DatasetAutoSplitConfirm, Strings.DatasetAutoSplit, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                    AutoSplitRequested?.Invoke((int)trainPercent.Value);
            };
            clearRetestItem.Click += (sender, args) => RetestRequested?.Invoke(SelectedImageIds, false);
            clearClassItem.Click += (sender, args) => ClearClassRequested?.Invoke(SelectedImageIds);
            clearSetItem.Click += (sender, args) => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Unassigned);
        }

        // 通知主視窗更新所選圖片的資料集狀態。
        public event Action<IReadOnlyList<string>, DatasetAssignmentStatus> DatasetRequested;
        // 通知主視窗更新所選圖片的 Retest 旗標。
        public event Action<IReadOnlyList<string>, bool> RetestRequested;
        // 通知主視窗清除所選圖片的 CLA 類別。
        public event Action<IReadOnlyList<string>> ClearClassRequested;
        // 通知主視窗以目前比例自動切分資料集。
        public event Action<int> AutoSplitRequested;

        // 取得目前已載入的全部資料集列。
        internal IReadOnlyList<DatasetRow> Rows { get { return rows; } }

        /// <summary>同步訓練與測試比例，使兩者總和維持百分之百。</summary>
        private void SynchronizePercentages(NumericUpDown source, NumericUpDown target)
        {
            if (synchronizingPercentages) return;
            synchronizingPercentages = true;
            target.Value = 100 - source.Value;
            synchronizingPercentages = false;
        }

        // 取得目前清單選取的圖片識別碼。
        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visibleRows[index].Image.Id).ToArray(); } }

        /// <summary>載入資料集列並更新類別篩選及可選的標註數欄位。</summary>
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

        /// <summary>依資料集、類別、標註及 Retest 篩選條件重建可見列。</summary>
        private void RefreshRows()
        {
            if (list == null) return;
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
            list.Invalidate();
        }

        /// <summary>將資料集列轉換為虛擬清單使用的顯示項目。</summary>
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

        /// <summary>將資料集分配狀態轉成畫面顯示文字。</summary>
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
        /// <summary>建立包含圖片、分配、類別及標註數量的資料列。</summary>
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
