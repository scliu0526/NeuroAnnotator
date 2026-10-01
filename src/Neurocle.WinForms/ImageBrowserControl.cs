using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class ImageBrowserControl : UserControl
    {
        private readonly TextBox search;
        private readonly ListView list;
        private readonly PictureBox preview;
        private readonly Label previewMessage;
        private readonly List<ProjectImage> source = new List<ProjectImage>();
        private readonly SemaphoreSlim previewGate = new SemaphoreSlim(1, 1);
        private IReadOnlyList<ProjectImage> visible = new List<ProjectImage>();
        private CancellationTokenSource previewLoad;
        private string imagesDirectory;
        private ImageListSortColumn column = ImageListSortColumn.Sequence;
        private ImageListSortDirection direction = ImageListSortDirection.Ascending;

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
            preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SystemColors.ControlDark };
            previewMessage = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            var previewPanel = new Panel { Dock = DockStyle.Fill };
            previewPanel.Controls.Add(preview);
            previewPanel.Controls.Add(previewMessage);
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 360, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(list);
            split.Panel1.Controls.Add(search);
            split.Panel2.Controls.Add(previewPanel);
            Controls.Add(split);
            ShowPreviewMessage(Strings.ImagePreviewEmpty);
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
            CancelPreviewLoad();
            visible = ImageListProjection.FilterAndSort(source, search.Text, column, direction);
            list.VirtualListSize = visible.Count;
            list.Invalidate();
            ShowPreviewMessage(Strings.ImagePreviewEmpty);
        }

        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs args)
        {
            ProjectImage image = visible[args.ItemIndex];
            args.Item = new ListViewItem(image.Sequence.ToString());
            args.Item.SubItems.Add(image.OriginalFileName);
        }

        private async void ShowSelectionAsync()
        {
            CancelPreviewLoad();
            if (list.SelectedIndices.Count == 0)
            {
                ShowPreviewMessage(Strings.ImagePreviewEmpty);
                return;
            }
            ProjectImage image = visible[list.SelectedIndices[0]];
            if (image.IsMissing)
            {
                ShowPreviewMessage(Strings.ImagePreviewMissing);
                return;
            }
            var cancellation = new CancellationTokenSource();
            previewLoad = cancellation;
            ShowPreviewMessage(Strings.ImagePreviewLoading);
            try
            {
                string path = Path.Combine(imagesDirectory, image.ManagedFileName);
                Size size = preview.ClientSize;
                Image rendered = await Task.Run(() => LoadPreview(path, size, cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested)
                {
                    rendered.Dispose();
                    return;
                }
                Image previous = preview.Image;
                preview.Image = rendered;
                previous?.Dispose();
                previewMessage.Visible = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!cancellation.IsCancellationRequested) ShowPreviewMessage(Strings.ImagePreviewUnavailable);
            }
            finally
            {
                if (ReferenceEquals(previewLoad, cancellation)) previewLoad = null;
                cancellation.Dispose();
            }
        }

        private Image LoadPreview(string path, Size availableSize, CancellationToken cancellation)
        {
            bool gateAcquired = false;
            try
            {
                previewGate.Wait(cancellation);
                gateAcquired = true;
                cancellation.ThrowIfCancellationRequested();
                using (var source = Image.FromFile(path))
                {
                    cancellation.ThrowIfCancellationRequested();
                    Size targetSize = CalculatePreviewSize(source.Size, availableSize);
                    var previewImage = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppPArgb);
                    try
                    {
                        using (Graphics graphics = Graphics.FromImage(previewImage))
                        {
                            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            graphics.DrawImage(source, new Rectangle(Point.Empty, targetSize));
                        }
                        cancellation.ThrowIfCancellationRequested();
                        return previewImage;
                    }
                    catch
                    {
                        previewImage.Dispose();
                        throw;
                    }
                }
            }
            finally
            {
                if (gateAcquired) previewGate.Release();
            }
        }

        private static Size CalculatePreviewSize(Size source, Size available)
        {
            int maximumWidth = Math.Max(1, available.Width);
            int maximumHeight = Math.Max(1, available.Height);
            double scale = Math.Min(1d, Math.Min((double)maximumWidth / source.Width, (double)maximumHeight / source.Height));
            return new Size(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        }

        private void CancelPreviewLoad()
        {
            previewLoad?.Cancel();
            previewLoad = null;
        }

        private void ShowPreviewMessage(string message)
        {
            Image previous = preview.Image;
            preview.Image = null;
            previous?.Dispose();
            previewMessage.Text = message;
            previewMessage.Visible = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPreviewLoad();
                preview.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
