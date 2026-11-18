using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class ImageBrowserControl
    {
        private System.Windows.Forms.TextBox search;
        private HorizontalWheelListView list;
        private System.Windows.Forms.ColumnHeader sequenceColumn;
        private System.Windows.Forms.ColumnHeader fileNameColumn;
        private System.Windows.Forms.ColumnHeader classColumn;
        private System.Windows.Forms.ColumnHeader annotationCountColumn;
        private System.Windows.Forms.ColumnHeader datasetColumn;
        private System.Windows.Forms.ColumnHeader retestColumn;

        private void InitializeComponent()
        {
            this.search = new System.Windows.Forms.TextBox();
            this.list = new HorizontalWheelListView();
            this.sequenceColumn = new System.Windows.Forms.ColumnHeader();
            this.fileNameColumn = new System.Windows.Forms.ColumnHeader();
            this.classColumn = new System.Windows.Forms.ColumnHeader();
            this.annotationCountColumn = new System.Windows.Forms.ColumnHeader();
            this.datasetColumn = new System.Windows.Forms.ColumnHeader();
            this.retestColumn = new System.Windows.Forms.ColumnHeader();
            this.SuspendLayout();
            //
            // search
            //
            this.search.AccessibleName = Strings.ImageSearchPlaceholder;
            this.search.Dock = System.Windows.Forms.DockStyle.Top;
            this.search.Name = "search";
            this.search.TabIndex = 1;
            this.search.TextChanged += new System.EventHandler(this.Search_TextChanged);
            //
            // list
            //
            this.sequenceColumn.Text = Strings.ImageSequenceColumn;
            this.sequenceColumn.Width = 50;
            this.fileNameColumn.Text = Strings.ImageFileNameColumn;
            this.fileNameColumn.Width = 130;
            this.classColumn.Text = Strings.ImageClassColumn;
            this.classColumn.Width = 85;
            this.list.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.sequenceColumn,
            this.fileNameColumn});
            this.list.Dock = System.Windows.Forms.DockStyle.Fill;
            this.list.FullRowSelect = true;
            this.list.HideSelection = false;
            this.list.Name = "list";
            this.list.TabIndex = 0;
            this.list.View = System.Windows.Forms.View.Details;
            this.list.VirtualMode = true;
            this.list.RetrieveVirtualItem += new System.Windows.Forms.RetrieveVirtualItemEventHandler(this.RetrieveVirtualItem);
            this.list.SelectedIndexChanged += new System.EventHandler(this.List_SelectedIndexChanged);
            this.list.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.List_ColumnClick);
            //
            // annotationCountColumn
            //
            this.annotationCountColumn.Text = Strings.ImageAnnotationCountColumn;
            this.annotationCountColumn.Width = 65;
            this.datasetColumn.Text = Strings.DatasetColumn;
            this.datasetColumn.Width = 80;
            this.retestColumn.Text = Strings.DatasetRetestColumn;
            this.retestColumn.Width = 60;
            //
            // ImageBrowserControl
            //
            this.Controls.Add(this.list);
            this.Controls.Add(this.search);
            this.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Name = "ImageBrowserControl";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
