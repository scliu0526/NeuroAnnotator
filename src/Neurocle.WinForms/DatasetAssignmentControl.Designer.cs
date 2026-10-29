using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class DatasetAssignmentControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListView list;
        private System.Windows.Forms.ComboBox setFilter;
        private System.Windows.Forms.ComboBox classFilter;
        private System.Windows.Forms.ComboBox annotationFilter;
        private System.Windows.Forms.ComboBox retestFilter;
        private System.Windows.Forms.NumericUpDown trainPercent;
        private System.Windows.Forms.NumericUpDown testPercent;
        private System.Windows.Forms.ColumnHeader sequenceColumn;
        private System.Windows.Forms.ColumnHeader fileNameColumn;
        private System.Windows.Forms.ColumnHeader datasetColumn;
        private System.Windows.Forms.ColumnHeader classColumn;
        private System.Windows.Forms.ColumnHeader annotatedColumn;
        private System.Windows.Forms.ColumnHeader annotationCountColumn;
        private System.Windows.Forms.ColumnHeader retestColumn;
        private System.Windows.Forms.Button trainButton;
        private System.Windows.Forms.Button testButton;
        private System.Windows.Forms.Button unassignedButton;
        private System.Windows.Forms.Button notUsedButton;
        private System.Windows.Forms.Button retestButton;
        private System.Windows.Forms.Button autoSplitButton;
        private System.Windows.Forms.ToolStripMenuItem clearRetestItem;
        private System.Windows.Forms.ToolStripMenuItem clearClassItem;
        private System.Windows.Forms.ToolStripMenuItem clearSetItem;
        private System.Windows.Forms.FlowLayoutPanel actions;
        private System.Windows.Forms.FlowLayoutPanel filters;
        private System.Windows.Forms.Label trainLabel;
        private System.Windows.Forms.Label testLabel;
        private System.Windows.Forms.Label setFilterLabel;
        private System.Windows.Forms.Label classFilterLabel;
        private System.Windows.Forms.Label annotationFilterLabel;
        private System.Windows.Forms.Label retestFilterLabel;
        private System.Windows.Forms.ContextMenuStrip datasetMenu;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.list = new System.Windows.Forms.ListView();
            this.setFilter = new System.Windows.Forms.ComboBox();
            this.classFilter = new System.Windows.Forms.ComboBox();
            this.annotationFilter = new System.Windows.Forms.ComboBox();
            this.retestFilter = new System.Windows.Forms.ComboBox();
            this.trainPercent = new System.Windows.Forms.NumericUpDown();
            this.testPercent = new System.Windows.Forms.NumericUpDown();
            this.sequenceColumn = new System.Windows.Forms.ColumnHeader();
            this.fileNameColumn = new System.Windows.Forms.ColumnHeader();
            this.datasetColumn = new System.Windows.Forms.ColumnHeader();
            this.classColumn = new System.Windows.Forms.ColumnHeader();
            this.annotatedColumn = new System.Windows.Forms.ColumnHeader();
            this.annotationCountColumn = new System.Windows.Forms.ColumnHeader();
            this.retestColumn = new System.Windows.Forms.ColumnHeader();
            this.trainButton = new System.Windows.Forms.Button();
            this.testButton = new System.Windows.Forms.Button();
            this.unassignedButton = new System.Windows.Forms.Button();
            this.notUsedButton = new System.Windows.Forms.Button();
            this.retestButton = new System.Windows.Forms.Button();
            this.autoSplitButton = new System.Windows.Forms.Button();
            this.actions = new System.Windows.Forms.FlowLayoutPanel();
            this.filters = new System.Windows.Forms.FlowLayoutPanel();
            this.trainLabel = new System.Windows.Forms.Label();
            this.testLabel = new System.Windows.Forms.Label();
            this.setFilterLabel = new System.Windows.Forms.Label();
            this.classFilterLabel = new System.Windows.Forms.Label();
            this.annotationFilterLabel = new System.Windows.Forms.Label();
            this.retestFilterLabel = new System.Windows.Forms.Label();
            this.datasetMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.clearRetestItem = new System.Windows.Forms.ToolStripMenuItem();
            this.clearClassItem = new System.Windows.Forms.ToolStripMenuItem();
            this.clearSetItem = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.trainPercent)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.testPercent)).BeginInit();
            this.SuspendLayout();
            //
            // actions
            //
            this.actions.AutoSize = true;
            this.actions.Dock = System.Windows.Forms.DockStyle.Top;
            this.actions.Name = "actions";
            this.actions.TabIndex = 1;
            //
            // trainButton
            //
            this.trainButton.AutoSize = true;
            this.trainButton.Name = "trainButton";
            this.trainButton.TabIndex = 0;
            this.trainButton.Text = Strings.DatasetTrain;
            this.trainButton.UseVisualStyleBackColor = true;
            //
            // testButton
            //
            this.testButton.AutoSize = true;
            this.testButton.Name = "testButton";
            this.testButton.TabIndex = 1;
            this.testButton.Text = Strings.DatasetTest;
            this.testButton.UseVisualStyleBackColor = true;
            //
            // unassignedButton
            //
            this.unassignedButton.AutoSize = true;
            this.unassignedButton.Name = "unassignedButton";
            this.unassignedButton.TabIndex = 2;
            this.unassignedButton.Text = Strings.DatasetUnassigned;
            this.unassignedButton.UseVisualStyleBackColor = true;
            //
            // notUsedButton
            //
            this.notUsedButton.AutoSize = true;
            this.notUsedButton.Name = "notUsedButton";
            this.notUsedButton.TabIndex = 3;
            this.notUsedButton.Text = Strings.DatasetNotUsed;
            this.notUsedButton.UseVisualStyleBackColor = true;
            //
            // retestButton
            //
            this.retestButton.AutoSize = true;
            this.retestButton.Name = "retestButton";
            this.retestButton.TabIndex = 4;
            this.retestButton.Text = Strings.DatasetRetest;
            this.retestButton.UseVisualStyleBackColor = true;
            //
            // trainLabel
            //
            this.trainLabel.AutoSize = true;
            this.trainLabel.Margin = new System.Windows.Forms.Padding(6, 8, 2, 2);
            this.trainLabel.TabIndex = 5;
            this.trainLabel.Text = Strings.DatasetTrainPercent;
            //
            // trainPercent
            //
            this.trainPercent.Minimum = 1;
            this.trainPercent.Maximum = 99;
            this.trainPercent.Value = 80;
            this.trainPercent.Width = 48;
            this.trainPercent.Name = "trainPercent";
            this.trainPercent.TabIndex = 6;
            //
            // testLabel
            //
            this.testLabel.AutoSize = true;
            this.testLabel.Margin = new System.Windows.Forms.Padding(2, 8, 2, 2);
            this.testLabel.TabIndex = 7;
            this.testLabel.Text = Strings.DatasetTestPercent;
            //
            // testPercent
            //
            this.testPercent.Minimum = 1;
            this.testPercent.Maximum = 99;
            this.testPercent.Value = 20;
            this.testPercent.Width = 48;
            this.testPercent.Name = "testPercent";
            this.testPercent.TabIndex = 8;
            //
            // autoSplitButton
            //
            this.autoSplitButton.AutoSize = true;
            this.autoSplitButton.Name = "autoSplitButton";
            this.autoSplitButton.TabIndex = 9;
            this.autoSplitButton.Text = Strings.DatasetAutoSplit;
            this.autoSplitButton.UseVisualStyleBackColor = true;
            //
            // actions controls
            //
            this.actions.Controls.Add(this.trainButton);
            this.actions.Controls.Add(this.testButton);
            this.actions.Controls.Add(this.unassignedButton);
            this.actions.Controls.Add(this.notUsedButton);
            this.actions.Controls.Add(this.retestButton);
            this.actions.Controls.Add(this.trainLabel);
            this.actions.Controls.Add(this.trainPercent);
            this.actions.Controls.Add(this.testLabel);
            this.actions.Controls.Add(this.testPercent);
            this.actions.Controls.Add(this.autoSplitButton);
            //
            // filters
            //
            this.filters.AutoSize = true;
            this.filters.Dock = System.Windows.Forms.DockStyle.Top;
            this.filters.Name = "filters";
            this.filters.TabIndex = 2;
            //
            // setFilterLabel
            //
            this.setFilterLabel.AutoSize = true;
            this.setFilterLabel.Margin = new System.Windows.Forms.Padding(6, 8, 2, 2);
            this.setFilterLabel.TabIndex = 0;
            this.setFilterLabel.Text = Strings.DatasetFilterSet;
            //
            // setFilter
            //
            this.setFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.setFilter.Items.AddRange(new object[] { Strings.DatasetFilterAll, Strings.DatasetTrain, Strings.DatasetTest, Strings.DatasetUnassigned, Strings.DatasetNotUsed });
            this.setFilter.SelectedIndex = 0;
            this.setFilter.Width = 105;
            this.setFilter.Name = "setFilter";
            this.setFilter.TabIndex = 1;
            //
            // classFilterLabel
            //
            this.classFilterLabel.AutoSize = true;
            this.classFilterLabel.Margin = new System.Windows.Forms.Padding(6, 8, 2, 2);
            this.classFilterLabel.TabIndex = 2;
            this.classFilterLabel.Text = Strings.DatasetFilterClass;
            //
            // classFilter
            //
            this.classFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.classFilter.Items.AddRange(new object[] { Strings.DatasetFilterAll });
            this.classFilter.SelectedIndex = 0;
            this.classFilter.Width = 105;
            this.classFilter.Name = "classFilter";
            this.classFilter.TabIndex = 3;
            //
            // annotationFilterLabel
            //
            this.annotationFilterLabel.AutoSize = true;
            this.annotationFilterLabel.Margin = new System.Windows.Forms.Padding(6, 8, 2, 2);
            this.annotationFilterLabel.TabIndex = 4;
            this.annotationFilterLabel.Text = Strings.DatasetFilterAnnotation;
            //
            // annotationFilter
            //
            this.annotationFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.annotationFilter.Items.AddRange(new object[] { Strings.DatasetFilterAll, Strings.DatasetFilterAnnotated, Strings.DatasetFilterEmpty });
            this.annotationFilter.SelectedIndex = 0;
            this.annotationFilter.Width = 105;
            this.annotationFilter.Name = "annotationFilter";
            this.annotationFilter.TabIndex = 5;
            //
            // retestFilterLabel
            //
            this.retestFilterLabel.AutoSize = true;
            this.retestFilterLabel.Margin = new System.Windows.Forms.Padding(6, 8, 2, 2);
            this.retestFilterLabel.TabIndex = 6;
            this.retestFilterLabel.Text = Strings.DatasetFilterRetest;
            //
            // retestFilter
            //
            this.retestFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.retestFilter.Items.AddRange(new object[] { Strings.DatasetFilterAll, Strings.DatasetFilterRetest, Strings.DatasetFilterNoRetest });
            this.retestFilter.SelectedIndex = 0;
            this.retestFilter.Width = 105;
            this.retestFilter.Name = "retestFilter";
            this.retestFilter.TabIndex = 7;
            //
            // filters controls
            //
            this.filters.Controls.Add(this.setFilterLabel);
            this.filters.Controls.Add(this.setFilter);
            this.filters.Controls.Add(this.classFilterLabel);
            this.filters.Controls.Add(this.classFilter);
            this.filters.Controls.Add(this.annotationFilterLabel);
            this.filters.Controls.Add(this.annotationFilter);
            this.filters.Controls.Add(this.retestFilterLabel);
            this.filters.Controls.Add(this.retestFilter);
            //
            // datasetMenu
            //
            this.datasetMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.clearRetestItem,
            this.clearClassItem,
            this.clearSetItem});
            //
            // clearRetestItem
            //
            this.clearRetestItem.Text = Strings.DatasetClearRetest;
            //
            // clearClassItem
            //
            this.clearClassItem.Text = Strings.DatasetClearClass;
            //
            // clearSetItem
            //
            this.clearSetItem.Text = Strings.DatasetClearSet;
            //
            // list
            //
            this.list.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.sequenceColumn,
            this.fileNameColumn,
            this.datasetColumn,
            this.classColumn,
            this.annotatedColumn,
            this.retestColumn});
            this.list.ContextMenuStrip = this.datasetMenu;
            this.list.Dock = System.Windows.Forms.DockStyle.Fill;
            this.list.FullRowSelect = true;
            this.list.HideSelection = false;
            this.list.MultiSelect = true;
            this.list.Name = "list";
            this.list.TabIndex = 0;
            this.list.View = System.Windows.Forms.View.Details;
            this.list.VirtualMode = true;
            //
            // columns
            //
            this.sequenceColumn.Text = Strings.ImageSequenceColumn;
            this.sequenceColumn.Width = 70;
            this.fileNameColumn.Text = Strings.ImageFileNameColumn;
            this.fileNameColumn.Width = 230;
            this.datasetColumn.Text = Strings.DatasetColumn;
            this.datasetColumn.Width = 100;
            this.classColumn.Text = Strings.DatasetClassColumn;
            this.classColumn.Width = 180;
            this.annotatedColumn.Text = Strings.DatasetAnnotatedColumn;
            this.annotatedColumn.Width = 80;
            this.annotationCountColumn.Text = Strings.DatasetAnnotationCountColumn;
            this.annotationCountColumn.Width = 80;
            this.retestColumn.Text = Strings.DatasetRetestColumn;
            this.retestColumn.Width = 70;
            //
            // DatasetAssignmentControl
            //
            this.Controls.Add(this.list);
            this.Controls.Add(this.actions);
            this.Controls.Add(this.filters);
            this.Name = "DatasetAssignmentControl";
            ((System.ComponentModel.ISupportInitialize)(this.trainPercent)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.testPercent)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
