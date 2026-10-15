using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class ImageBrowserControl : UserControl
    {
        private readonly TextBox search;
        private readonly ListView list;
        private readonly ColumnHeader annotationCountColumn;
        private readonly List<ImageListRow> source = new List<ImageListRow>();
        private IReadOnlyList<ImageListRow> visible = new List<ImageListRow>();
        private string imagesDirectory;
        private ImageListSortColumn column = ImageListSortColumn.Sequence;
        private ImageListSortDirection direction = ImageListSortDirection.Ascending;
        private bool refreshing;

        public event EventHandler<ImageSelectedEventArgs> ImageSelected;

        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visible[index].Image.Id).ToArray(); } }
        internal IReadOnlyList<ImageListRow> Rows { get { return source; } }

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

        internal void SelectImage(string imageId)
        {
            int index = visible.Select((row, current) => new { row, current }).Where(item => item.row.Image.Id == imageId).Select(item => item.current).DefaultIfEmpty(-1).Single();
            list.SelectedIndices.Clear();
            if (index >= 0) list.SelectedIndices.Add(index);
        }

        public ImageBrowserControl()
        {
            Dock = DockStyle.Fill;
            search = new TextBox { Dock = DockStyle.Top, AccessibleName = Strings.ImageSearchPlaceholder };
            search.TextChanged += (sender, args) => RefreshList();
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false };
            list.Columns.Add(Strings.ImageSequenceColumn, 80);
            list.Columns.Add(Strings.ImageFileNameColumn, 260);
            list.Columns.Add(Strings.ImageClassColumn, 180);
            annotationCountColumn = new ColumnHeader { Text = Strings.ImageAnnotationCountColumn, Width = 90 };
            list.RetrieveVirtualItem += RetrieveVirtualItem;
            list.SelectedIndexChanged += (sender, args) => ShowSelectionAsync();
            list.ColumnClick += (sender, args) => ChangeSort(args.Column == 0 ? ImageListSortColumn.Sequence : args.Column == 1 ? ImageListSortColumn.FileName : args.Column == 2 ? ImageListSortColumn.Classes : ImageListSortColumn.AnnotationCount);
            Controls.Add(list);
            Controls.Add(search);
        }

        public void LoadImages(IReadOnlyList<ProjectImage> images, string managedImagesDirectory)
        {
            LoadRows((images ?? new ProjectImage[0]).Select(image => new ImageListRow(image, new string[0], 0, false)), managedImagesDirectory);
        }

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

        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs args)
        {
            ImageListRow row = visible[args.ItemIndex];
            args.Item = new ListViewItem(row.Image.Sequence.ToString());
            args.Item.SubItems.Add(row.Image.OriginalFileName);
            args.Item.SubItems.Add(row.ClassDisplay);
            if (list.Columns.Contains(annotationCountColumn)) args.Item.SubItems.Add(row.AnnotationCount.ToString());
        }

        private void ShowSelectionAsync()
        {
            if (refreshing || list.SelectedIndices.Count == 0) return;
            ProjectImage image = visible[list.SelectedIndices[0]].Image;
            ImageSelected?.Invoke(this, new ImageSelectedEventArgs(image, Path.Combine(imagesDirectory, image.ManagedFileName)));
        }
    }

    internal sealed class ImageSelectedEventArgs : EventArgs
    {
        public ImageSelectedEventArgs(ProjectImage image, string path)
        {
            Image = image;
            Path = path;
        }

        public ProjectImage Image { get; private set; }
        public string Path { get; private set; }
    }
}
