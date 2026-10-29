using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed partial class DatasetAssignmentControl : UserControl
    {
        private bool synchronizingPercentages;
        private readonly List<DatasetRow> rows = new List<DatasetRow>();
        private readonly List<DatasetRow> visibleRows = new List<DatasetRow>();

        public DatasetAssignmentControl()
        {
            InitializeComponent();
            WireRuntimeEvents();
        }

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

        public event Action<IReadOnlyList<string>, DatasetAssignmentStatus> DatasetRequested;
        public event Action<IReadOnlyList<string>, bool> RetestRequested;
        public event Action<IReadOnlyList<string>> ClearClassRequested;
        public event Action<int> AutoSplitRequested;

        internal IReadOnlyList<DatasetRow> Rows { get { return rows; } }

        private void SynchronizePercentages(NumericUpDown source, NumericUpDown target)
        {
            if (synchronizingPercentages) return;
            synchronizingPercentages = true;
            target.Value = 100 - source.Value;
            synchronizingPercentages = false;
        }

        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visibleRows[index].Image.Id).ToArray(); } }

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

        private static string DatasetText(DatasetAssignmentStatus status)
        {
            if (status == DatasetAssignmentStatus.Train) return Strings.DatasetTrain;
            if (status == DatasetAssignmentStatus.Test) return Strings.DatasetTest;
            if (status == DatasetAssignmentStatus.NotUsed) return Strings.DatasetNotUsed;
            return Strings.DatasetUnassigned;
        }
    }

    internal sealed class DatasetRow
    {
        public DatasetRow(ProjectImage image, DatasetAssignment assignment, IReadOnlyList<string> classNames, int annotationCount)
        {
            Image = image; Assignment = assignment; ClassNames = classNames ?? throw new ArgumentNullException(nameof(classNames)); AnnotationCount = annotationCount;
        }
        public ProjectImage Image { get; private set; }
        public DatasetAssignment Assignment { get; private set; }
        public IReadOnlyList<string> ClassNames { get; private set; }
        public int AnnotationCount { get; private set; }
        public bool HasAnnotation { get { return AnnotationCount > 0; } }
    }
}
