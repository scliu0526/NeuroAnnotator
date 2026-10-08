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
        private readonly List<ProjectImage> source = new List<ProjectImage>();
        private IReadOnlyList<ProjectImage> visible = new List<ProjectImage>();
        private string imagesDirectory;
        private ImageListSortColumn column = ImageListSortColumn.Sequence;
        private ImageListSortDirection direction = ImageListSortDirection.Ascending;

        public event EventHandler<ImageSelectedEventArgs> ImageSelected;

        public IReadOnlyList<string> SelectedImageIds { get { return list.SelectedIndices.Cast<int>().Select(index => visible[index].Id).ToArray(); } }

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

        public ImageBrowserControl()
        {
            Dock = DockStyle.Fill;
            search = new TextBox { Dock = DockStyle.Top, AccessibleName = Strings.ImageSearchPlaceholder };
            search.TextChanged += (sender, args) => RefreshList();
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false };
            list.Columns.Add(Strings.ImageSequenceColumn, 80);
            list.Columns.Add(Strings.ImageFileNameColumn, 260);
            list.RetrieveVirtualItem += RetrieveVirtualItem;
            list.SelectedIndexChanged += (sender, args) => ShowSelectionAsync();
            list.ColumnClick += (sender, args) => ChangeSort(args.Column == 0 ? ImageListSortColumn.Sequence : ImageListSortColumn.FileName);
            Controls.Add(list);
            Controls.Add(search);
        }

        public void LoadImages(IReadOnlyList<ProjectImage> images, string managedImagesDirectory)
        {
            source.Clear();
            if (images != null) source.AddRange(images);
            imagesDirectory = managedImagesDirectory;
            RefreshList();
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
            string selectedId = list.SelectedIndices.Count == 0 ? null : visible[list.SelectedIndices[0]].Id;
            visible = ImageListProjection.FilterAndSort(source, search.Text, column, direction);
            list.VirtualListSize = visible.Count;
            list.SelectedIndices.Clear();
            int selectedIndex = selectedId == null ? -1 : visible.Select((image, index) => new { image, index }).Where(item => item.image.Id == selectedId).Select(item => item.index).DefaultIfEmpty(-1).Single();
            if (selectedIndex >= 0) list.SelectedIndices.Add(selectedIndex);
            list.Invalidate();
        }

        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs args)
        {
            ProjectImage image = visible[args.ItemIndex];
            args.Item = new ListViewItem(image.Sequence.ToString());
            args.Item.SubItems.Add(image.OriginalFileName);
        }

        private void ShowSelectionAsync()
        {
            if (list.SelectedIndices.Count == 0) return;
            ProjectImage image = visible[list.SelectedIndices[0]];
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
