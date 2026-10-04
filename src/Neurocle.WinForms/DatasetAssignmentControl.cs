using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class DatasetAssignmentControl : UserControl
    {
        private readonly ListView list;
        private readonly ComboBox setFilter;
        private readonly ComboBox classFilter;
        private readonly ComboBox annotationFilter;
        private readonly ComboBox retestFilter;
        private readonly List<DatasetRow> rows = new List<DatasetRow>();
        private readonly List<DatasetRow> visibleRows = new List<DatasetRow>();

        public DatasetAssignmentControl()
        {
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            AddButton(actions, Strings.DatasetTrain, () => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Train));
            AddButton(actions, Strings.DatasetTest, () => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Test));
            AddButton(actions, Strings.DatasetUnassigned, () => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Unassigned));
            AddButton(actions, Strings.DatasetNotUsed, () => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.NotUsed));
            AddButton(actions, Strings.DatasetRetest, () => RetestRequested?.Invoke(SelectedImageIds, true));
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            setFilter = AddFilter(filters, Strings.DatasetFilterSet, new[] { Strings.DatasetFilterAll, Strings.DatasetTrain, Strings.DatasetTest, Strings.DatasetUnassigned, Strings.DatasetNotUsed });
            classFilter = AddFilter(filters, Strings.DatasetFilterClass, new[] { Strings.DatasetFilterAll });
            annotationFilter = AddFilter(filters, Strings.DatasetFilterAnnotation, new[] { Strings.DatasetFilterAll, Strings.DatasetFilterAnnotated, Strings.DatasetFilterEmpty });
            retestFilter = AddFilter(filters, Strings.DatasetFilterRetest, new[] { Strings.DatasetFilterAll, Strings.DatasetFilterRetest, Strings.DatasetFilterNoRetest });
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false, VirtualMode = true };
            list.RetrieveVirtualItem += (sender, args) => args.Item = CreateItem(visibleRows[args.ItemIndex]);
            list.Columns.Add(Strings.ImageSequenceColumn, 70);
            list.Columns.Add(Strings.ImageFileNameColumn, 230);
            list.Columns.Add(Strings.DatasetColumn, 100);
            list.Columns.Add(Strings.DatasetClassColumn, 180);
            list.Columns.Add(Strings.DatasetAnnotatedColumn, 80);
            list.Columns.Add(Strings.DatasetRetestColumn, 70);
            var menu = new ContextMenuStrip();
            menu.Items.Add(Strings.DatasetClearRetest, null, (sender, args) => RetestRequested?.Invoke(SelectedImageIds, false));
            menu.Items.Add(Strings.DatasetClearClass, null, (sender, args) => ClearClassRequested?.Invoke(SelectedImageIds));
            menu.Items.Add(Strings.DatasetClearSet, null, (sender, args) => DatasetRequested?.Invoke(SelectedImageIds, DatasetAssignmentStatus.Unassigned));
            list.ContextMenuStrip = menu;
            Controls.Add(list);
            Controls.Add(actions);
            Controls.Add(filters);
        }

        public event Action<IReadOnlyList<string>, DatasetAssignmentStatus> DatasetRequested;
        public event Action<IReadOnlyList<string>, bool> RetestRequested;
        public event Action<IReadOnlyList<string>> ClearClassRequested;

        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visibleRows[index].Image.Id).ToArray(); } }

        public void LoadRows(IEnumerable<DatasetRow> values)
        {
            rows.Clear();
            rows.AddRange(values ?? Enumerable.Empty<DatasetRow>());
            string selectedClass = classFilter.SelectedItem as string;
            classFilter.Items.Clear();
            classFilter.Items.Add(Strings.DatasetFilterAll);
            foreach (string value in rows.SelectMany(row => row.ClassNames).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)) classFilter.Items.Add(value);
            classFilter.SelectedItem = classFilter.Items.Contains(selectedClass) ? selectedClass : Strings.DatasetFilterAll;
            RefreshRows();
        }

        private static ComboBox AddFilter(Control parent, string label, IEnumerable<string> values)
        {
            parent.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 8, 2, 2) });
            var result = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
            result.Items.AddRange(values.Cast<object>().ToArray());
            result.SelectedIndex = 0;
            result.SelectedIndexChanged += (sender, args) => ((DatasetAssignmentControl)parent.Parent).RefreshRows();
            parent.Controls.Add(result);
            return result;
        }

        private static void AddButton(Control parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (sender, args) => action();
            parent.Controls.Add(button);
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
        public DatasetRow(ProjectImage image, DatasetAssignment assignment, IReadOnlyList<string> classNames)
        {
            Image = image; Assignment = assignment; ClassNames = classNames ?? throw new ArgumentNullException(nameof(classNames));
        }
        public ProjectImage Image { get; private set; }
        public DatasetAssignment Assignment { get; private set; }
        public IReadOnlyList<string> ClassNames { get; private set; }
        public bool HasAnnotation { get { return ClassNames.Count > 0; } }
    }
}
