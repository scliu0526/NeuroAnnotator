using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class AnnotationJsonImportDialog
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.CheckBox annotationsAndClasses;
        private System.Windows.Forms.ComboBox annotationMode;
        private System.Windows.Forms.CheckBox dataset;
        private System.Windows.Forms.ComboBox datasetMode;
        private System.Windows.Forms.TreeView preview;
        private System.Windows.Forms.Button importButton;
        private System.Windows.Forms.Label sourceFile;
        private System.Windows.Forms.Panel unknownPanel;
        private System.Windows.Forms.ListBox unknownFields;
        private System.Windows.Forms.CheckBox allowUnknownFields;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.annotationsAndClasses = new System.Windows.Forms.CheckBox();
            this.annotationMode = new System.Windows.Forms.ComboBox();
            this.dataset = new System.Windows.Forms.CheckBox();
            this.datasetMode = new System.Windows.Forms.ComboBox();
            this.preview = new System.Windows.Forms.TreeView();
            this.importButton = new System.Windows.Forms.Button();
            this.sourceFile = new System.Windows.Forms.Label();
            this.unknownPanel = new System.Windows.Forms.Panel();
            this.unknownFields = new System.Windows.Forms.ListBox();
            this.allowUnknownFields = new System.Windows.Forms.CheckBox();
            var cancelButton = new System.Windows.Forms.Button();
            var options = new System.Windows.Forms.TableLayoutPanel();
            var previewGroup = new System.Windows.Forms.GroupBox();
            var content = new System.Windows.Forms.TableLayoutPanel();
            var actions = new System.Windows.Forms.FlowLayoutPanel();
            this.SuspendLayout();
            this.annotationsAndClasses.Text = "標註與類別";
            this.annotationsAndClasses.Checked = true;
            this.annotationsAndClasses.AutoSize = true;
            this.annotationsAndClasses.CheckedChanged += new System.EventHandler(this.SelectionChanged);
            this.annotationMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.annotationMode.Width = 220;
            this.dataset.Text = "資料集狀態";
            this.dataset.Checked = true;
            this.dataset.AutoSize = true;
            this.dataset.CheckedChanged += new System.EventHandler(this.SelectionChanged);
            this.datasetMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.datasetMode.Width = 220;
            this.datasetMode.Items.AddRange(new object[] { "只加入未分配圖片", "取代所有對應圖片" });
            this.preview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.preview.HideSelection = false;
            this.preview.ShowLines = false;
            this.preview.ShowPlusMinus = true;
            this.preview.ShowRootLines = false;
            this.importButton.Text = Strings.ImportAnnotationJson;
            this.importButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.importButton.AutoSize = true;
            cancelButton.Text = Strings.Cancel;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.AutoSize = true;
            this.sourceFile.AutoSize = true;
            this.sourceFile.Text = "匯入檔案：";
            this.sourceFile.Margin = new System.Windows.Forms.Padding(3, 3, 3, 18);
            this.unknownFields.Dock = System.Windows.Forms.DockStyle.Fill;
            this.allowUnknownFields.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.allowUnknownFields.AutoSize = true;
            this.allowUnknownFields.Text = "忽略以上未知欄位並繼續";
            this.allowUnknownFields.CheckedChanged += new System.EventHandler(this.SelectionChanged);
            this.unknownPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.unknownPanel.Height = 120;
            this.unknownPanel.Controls.Add(this.unknownFields);
            this.unknownPanel.Controls.Add(this.allowUnknownFields);
            this.unknownPanel.Visible = false;
            options.Dock = System.Windows.Forms.DockStyle.Fill;
            options.ColumnCount = 1;
            options.RowCount = 6;
            options.Padding = new System.Windows.Forms.Padding(12);
            options.Controls.Add(this.sourceFile, 0, 0);
            options.Controls.Add(this.annotationsAndClasses, 0, 1);
            options.Controls.Add(this.annotationMode, 0, 2);
            options.Controls.Add(this.dataset, 0, 3);
            options.Controls.Add(this.datasetMode, 0, 4);
            options.Controls.Add(this.unknownPanel, 0, 5);
            options.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            options.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            options.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            options.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            options.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            options.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
            previewGroup.Text = "檔案內容預覽";
            previewGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            previewGroup.Padding = new System.Windows.Forms.Padding(10);
            previewGroup.Controls.Add(this.preview);
            content.Dock = System.Windows.Forms.DockStyle.Fill;
            content.ColumnCount = 2;
            content.Padding = new System.Windows.Forms.Padding(12);
            content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 44));
            content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 56));
            content.Controls.Add(options, 0, 0);
            content.Controls.Add(previewGroup, 1, 0);
            actions.Dock = System.Windows.Forms.DockStyle.Bottom;
            actions.Height = 48;
            actions.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            actions.Padding = new System.Windows.Forms.Padding(8);
            actions.Controls.Add(this.importButton);
            actions.Controls.Add(cancelButton);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(760, 430);
            this.MinimumSize = new System.Drawing.Size(680, 400);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Strings.ImportAnnotationJson;
            this.Controls.Add(content);
            this.Controls.Add(actions);
            this.AcceptButton = this.importButton;
            this.CancelButton = cancelButton;
            this.ResumeLayout(false);
        }
    }
}
