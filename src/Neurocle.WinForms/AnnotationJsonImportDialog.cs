using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class AnnotationJsonPreflightResult
    {
        public AnnotationJsonPreflightResult(AnnotationJsonImportPreview preview, string errorMessage)
        {
            Preview = preview;
            ErrorMessage = errorMessage;
        }

        public AnnotationJsonImportPreview Preview { get; }
        public string ErrorMessage { get; }
    }

    internal sealed partial class AnnotationJsonImportDialog : Form
    {
        private readonly AnnotationSetType targetType;
        private readonly Func<string, AnnotationJsonPreflightResult> preflight;
        private AnnotationJsonImportPreview sourcePreview;
        private int selectionVersion;

        public AnnotationJsonImportDialog(AnnotationSetType targetAnnotationSetType, Func<string, AnnotationJsonPreflightResult> preflightJson)
        {
            InitializeComponent();
            targetType = targetAnnotationSetType;
            preflight = preflightJson ?? throw new ArgumentNullException(nameof(preflightJson));
            annotationMode.Items.Add(Strings.ImportAddToEmptyImages);
            if (targetType != AnnotationSetType.Cla) annotationMode.Items.Add(Strings.ImportAddToAllImages);
            annotationMode.Items.Add(Strings.ImportReplaceMatchingImages);
            annotationMode.SelectedIndex = 0;
            datasetMode.SelectedIndex = 0;
            UpdateSelectionState();
        }

        public string FilePath { get; private set; }
        public AnnotationJsonImportPreview SourcePreview => sourcePreview;
        public bool ImportAnnotationsAndClasses => annotationsAndClasses.Checked;
        public bool ImportDataset => dataset.Checked;
        public bool AllowUnknownFields => sourcePreview != null && sourcePreview.UnknownFields.Count > 0 && allowUnknownFields.Checked;

        public AnnotationImportMode AnnotationMode
        {
            get
            {
                if (annotationMode.SelectedIndex < 0) return AnnotationImportMode.AddToEmptyImages;
                if (targetType != AnnotationSetType.Cla && annotationMode.SelectedIndex == 1) return AnnotationImportMode.AddToAllImages;
                bool replaceSelected = targetType == AnnotationSetType.Cla ? annotationMode.SelectedIndex == 1 : annotationMode.SelectedIndex == 2;
                return replaceSelected ? AnnotationImportMode.ReplaceMatchingImages : AnnotationImportMode.AddToEmptyImages;
            }
        }

        public DatasetImportMode DatasetMode => datasetMode.SelectedIndex == 1 ? DatasetImportMode.ReplaceMatchingImages : DatasetImportMode.AddToUnassignedImages;

        internal async Task SelectFileAsync(string filePath)
        {
            int version = ++selectionVersion;
            FilePath = filePath;
            sourcePath.Text = String.IsNullOrWhiteSpace(filePath) ? String.Empty : Path.GetFileName(filePath);
            sourcePreview = null;
            ResetPreview();
            validationMessage.Text = String.IsNullOrWhiteSpace(filePath) ? String.Empty : Strings.ImportJsonValidating;
            validationMessage.Visible = validationMessage.Text.Length > 0;
            UpdateSelectionState();
            if (String.IsNullOrWhiteSpace(filePath)) return;

            AnnotationJsonPreflightResult result;
            try { result = await Task.Run(() => preflight(filePath)); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonAnnotationImportException)
            {
                result = new AnnotationJsonPreflightResult(null, exception.Message);
            }
            if (IsDisposed || version != selectionVersion) return;

            sourcePreview = result?.Preview;
            if (sourcePreview != null) PopulatePreview(sourcePreview);
            validationMessage.Text = result?.ErrorMessage ?? String.Empty;
            validationMessage.Visible = validationMessage.Text.Length > 0;
            unknownFields.Items.Clear();
            if (sourcePreview != null) unknownFields.Items.AddRange(sourcePreview.UnknownFields.Cast<object>().ToArray());
            unknownPanel.Visible = sourcePreview != null && sourcePreview.UnknownFields.Count > 0;
            UpdateSelectionState();
        }

        private async void ChooseFileButton_Click(object sender, EventArgs args)
        {
            using (var dialog = new OpenFileDialog { Filter = Strings.AnnotationJsonFileFilter, Multiselect = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) await SelectFileAsync(dialog.FileName);
            }
        }

        private async void ClearFileButton_Click(object sender, EventArgs args)
        {
            await SelectFileAsync(null);
        }

        private void SelectionChanged(object sender, EventArgs args) { UpdateSelectionState(); }

        private void UpdateSelectionState()
        {
            bool validFile = sourcePreview != null && String.IsNullOrEmpty(validationMessage.Text);
            annotationsAndClasses.Enabled = validFile;
            dataset.Enabled = validFile;
            annotationMode.Enabled = validFile && annotationsAndClasses.Checked;
            datasetMode.Enabled = validFile && dataset.Checked;
            allowUnknownFields.Enabled = validFile && sourcePreview != null && sourcePreview.UnknownFields.Count > 0;
            bool unknownFieldsAccepted = sourcePreview == null || sourcePreview.UnknownFields.Count == 0 || allowUnknownFields.Checked;
            importButton.Enabled = validFile && (annotationsAndClasses.Checked || dataset.Checked) && unknownFieldsAccepted;
        }

        private void PopulatePreview(AnnotationJsonImportPreview value)
        {
            ResetPreview();
            preview.Nodes.Add("JSON type: " + TypeLabel(value.Type));
            TreeNode classes = preview.Nodes.Add("CLASS: " + value.ClassNames.Count);
            foreach (string className in value.ClassNames) classes.Nodes.Add(className);
            preview.Nodes.Add("SET: " + String.Join(", ", SetLabels(value)));
            classes.Expand();
        }

        private void ResetPreview()
        {
            preview.Nodes.Clear();
            unknownFields.Items.Clear();
            unknownPanel.Visible = false;
            allowUnknownFields.Checked = false;
        }

        private static string TypeLabel(AnnotationSetType value)
        {
            switch (value)
            {
                case AnnotationSetType.Cla: return "CLA";
                case AnnotationSetType.Obd: return "OBD";
                case AnnotationSetType.Seg: return "SEG";
                default: return value.ToString();
            }
        }

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
    }
}
