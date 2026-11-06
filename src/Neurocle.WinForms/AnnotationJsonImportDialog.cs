using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.WinForms
{
    internal sealed partial class AnnotationJsonImportDialog : Form
    {
        private AnnotationJsonImportPreview sourcePreview;

        public AnnotationJsonImportDialog() { InitializeComponent(); }

        public AnnotationJsonImportDialog(string filePath, AnnotationJsonImportPreview value) : this()
        {
            sourcePreview = value ?? throw new ArgumentNullException(nameof(value));
            sourceFile.Text = "匯入檔案：" + Path.GetFileName(filePath);
            annotationMode.Items.Add("只加入空白圖片");
            if (value.Type != AnnotationSetType.Cla) annotationMode.Items.Add("加入所有圖片");
            annotationMode.Items.Add("取代所有對應圖片");
            annotationMode.SelectedIndex = 0;
            datasetMode.SelectedIndex = 0;
            TreeNode classes = preview.Nodes.Add("Class: " + value.ClassNames.Count);
            foreach (string className in value.ClassNames) classes.Nodes.Add(className);
            preview.Nodes.Add("Set: " + String.Join(", ", SetLabels(value)));
            classes.Expand();
            unknownFields.Items.AddRange(value.UnknownFields.Cast<object>().ToArray());
            unknownPanel.Visible = value.UnknownFields.Count > 0;
            UpdateSelectionState();
        }

        public bool ImportAnnotationsAndClasses => annotationsAndClasses.Checked;
        public bool ImportDataset => dataset.Checked;
        public bool AllowUnknownFields => sourcePreview != null && sourcePreview.UnknownFields.Count > 0 && allowUnknownFields.Checked;

        public AnnotationImportMode AnnotationMode
        {
            get
            {
                if (annotationMode.SelectedItem.ToString() == "加入所有圖片") return AnnotationImportMode.AddToAllImages;
                return annotationMode.SelectedItem.ToString() == "取代所有對應圖片" ? AnnotationImportMode.ReplaceMatchingImages : AnnotationImportMode.AddToEmptyImages;
            }
        }

        public DatasetImportMode DatasetMode => datasetMode.SelectedIndex == 1 ? DatasetImportMode.ReplaceMatchingImages : DatasetImportMode.AddToUnassignedImages;

        private static IReadOnlyList<string> SetLabels(AnnotationJsonImportPreview value)
        {
            var labels = new List<string>();
            if (value.SetValues.Contains("not_used")) labels.Add("Not used");
            if (value.HasRetest) labels.Add("Retest");
            if (value.SetValues.Contains("train")) labels.Add("Train");
            if (value.SetValues.Contains("test")) labels.Add("Test");
            if (value.SetValues.Contains(String.Empty)) labels.Add("Not assigned");
            return labels;
        }

        private void SelectionChanged(object sender, EventArgs args) { UpdateSelectionState(); }

        private void UpdateSelectionState()
        {
            annotationMode.Enabled = annotationsAndClasses.Checked;
            datasetMode.Enabled = dataset.Checked;
            bool unknownFieldsAccepted = sourcePreview == null || sourcePreview.UnknownFields.Count == 0 || allowUnknownFields.Checked;
            importButton.Enabled = (annotationsAndClasses.Checked || dataset.Checked) && unknownFieldsAccepted;
        }
    }
}
