using System.Drawing;
using System.Windows.Forms;
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
        private System.Windows.Forms.TextBox sourcePath;
        private System.Windows.Forms.Button chooseFileButton;
        private System.Windows.Forms.Button clearFileButton;
        private System.Windows.Forms.Label validationMessage;
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
            this.sourcePath = new System.Windows.Forms.TextBox();
            this.chooseFileButton = new System.Windows.Forms.Button();
            this.clearFileButton = new System.Windows.Forms.Button();
            this.validationMessage = new System.Windows.Forms.Label();
            this.unknownPanel = new System.Windows.Forms.Panel();
            this.unknownFields = new System.Windows.Forms.ListBox();
            this.allowUnknownFields = new System.Windows.Forms.CheckBox();
            var cancelButton = new System.Windows.Forms.Button();
            var fileRow = new System.Windows.Forms.TableLayoutPanel();
            var options = new System.Windows.Forms.TableLayoutPanel();
            var previewGroup = new System.Windows.Forms.GroupBox();
            var content = new System.Windows.Forms.TableLayoutPanel();
            var actions = new System.Windows.Forms.FlowLayoutPanel();
            this.SuspendLayout();

            fileRow.Dock = DockStyle.Fill;
            fileRow.ColumnCount = 4;
            fileRow.RowCount = 1;
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var importFileLabel = new Label { Text = Strings.ImportFile, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) };
            this.sourcePath.Dock = DockStyle.Fill;
            this.sourcePath.ReadOnly = true;
            this.sourcePath.Name = "sourcePath";
            this.clearFileButton.Text = Strings.Clear;
            this.clearFileButton.AutoSize = true;
            this.clearFileButton.Name = "clearFileButton";
            this.clearFileButton.Click += new System.EventHandler(this.ClearFileButton_Click);
            this.chooseFileButton.Text = Strings.ChooseFiles;
            this.chooseFileButton.AutoSize = true;
            this.chooseFileButton.Name = "chooseFileButton";
            this.chooseFileButton.Click += new System.EventHandler(this.ChooseFileButton_Click);
            fileRow.Controls.Add(importFileLabel, 0, 0);
            fileRow.Controls.Add(this.sourcePath, 1, 0);
            fileRow.Controls.Add(this.clearFileButton, 2, 0);
            fileRow.Controls.Add(this.chooseFileButton, 3, 0);

            this.validationMessage.AutoSize = true;
            this.validationMessage.ForeColor = Color.Firebrick;
            this.validationMessage.MaximumSize = new Size(330, 0);
            this.validationMessage.Name = "validationMessage";
            this.validationMessage.Visible = false;

            this.annotationsAndClasses.Text = Strings.ImportClassAndAnnotations;
            this.annotationsAndClasses.Checked = true;
            this.annotationsAndClasses.AutoSize = true;
            this.annotationsAndClasses.Name = "annotationsAndClasses";
            this.annotationsAndClasses.CheckedChanged += new System.EventHandler(this.SelectionChanged);
            this.annotationMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.annotationMode.Width = 250;
            this.annotationMode.Name = "annotationMode";
            this.dataset.Text = Strings.ImportDatasetAndRetest;
            this.dataset.Checked = true;
            this.dataset.AutoSize = true;
            this.dataset.Name = "dataset";
            this.dataset.CheckedChanged += new System.EventHandler(this.SelectionChanged);
            this.datasetMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.datasetMode.Width = 250;
            this.datasetMode.Items.AddRange(new object[] { Strings.ImportAddToUnassignedImages, Strings.ImportReplaceMatchingImages });
            this.datasetMode.Name = "datasetMode";

            this.unknownFields.Dock = System.Windows.Forms.DockStyle.Fill;
            this.unknownFields.Name = "unknownFields";
            this.allowUnknownFields.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.allowUnknownFields.AutoSize = true;
            this.allowUnknownFields.Name = "allowUnknownFields";
            this.allowUnknownFields.Text = Strings.ImportIgnoreUnknownFields;
            this.allowUnknownFields.CheckedChanged += new System.EventHandler(this.SelectionChanged);
            this.unknownPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.unknownPanel.Height = 120;
            this.unknownPanel.Name = "unknownPanel";
            this.unknownPanel.Controls.Add(this.unknownFields);
            this.unknownPanel.Controls.Add(this.allowUnknownFields);
            this.unknownPanel.Visible = false;

            options.Dock = System.Windows.Forms.DockStyle.Fill;
            options.ColumnCount = 1;
            options.RowCount = 7;
            options.Padding = new System.Windows.Forms.Padding(12);
            options.Controls.Add(fileRow, 0, 0);
            options.Controls.Add(this.validationMessage, 0, 1);
            options.Controls.Add(this.annotationsAndClasses, 0, 2);
            options.Controls.Add(this.annotationMode, 0, 3);
            options.Controls.Add(this.dataset, 0, 4);
            options.Controls.Add(this.datasetMode, 0, 5);
            options.Controls.Add(this.unknownPanel, 0, 6);
            for (int row = 0; row < 6; row++) options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            this.preview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.preview.HideSelection = false;
            this.preview.ShowLines = false;
            this.preview.ShowPlusMinus = true;
            this.preview.ShowRootLines = false;
            this.preview.Name = "preview";
            previewGroup.Text = Strings.ImportJsonPreview;
            previewGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            previewGroup.Padding = new System.Windows.Forms.Padding(10);
            previewGroup.Controls.Add(this.preview);

            content.Dock = System.Windows.Forms.DockStyle.Fill;
            content.ColumnCount = 2;
            content.Padding = new System.Windows.Forms.Padding(12);
            content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 48));
            content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 52));
            content.Controls.Add(options, 0, 0);
            content.Controls.Add(previewGroup, 1, 0);

            this.importButton.Text = Strings.ImportAnnotationJson;
            this.importButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.importButton.AutoSize = true;
            this.importButton.Enabled = false;
            cancelButton.Text = Strings.Cancel;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.AutoSize = true;
            actions.Dock = System.Windows.Forms.DockStyle.Bottom;
            actions.Height = 48;
            actions.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            actions.Padding = new System.Windows.Forms.Padding(8);
            actions.Controls.Add(this.importButton);
            actions.Controls.Add(cancelButton);

            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(900, 500);
            this.MinimumSize = new System.Drawing.Size(760, 440);
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
