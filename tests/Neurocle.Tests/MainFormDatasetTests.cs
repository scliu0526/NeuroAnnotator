using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Application.Projects;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms;

namespace Neurocle.Tests
{
    [TestClass]
    public class MainFormDatasetTests
    {
        [TestMethod]
        public void Undo_refreshes_dataset_annotation_counts_for_the_selected_set()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string source = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(source, Color.Red);
                store = ProjectStore.Create(path);
                ProjectImage image = store.ImportFiles(new[] { source }).Single();
                AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                store.AddObdAnnotation(set.Id, image.Id, store.GetClasses(set.Id).Single().Id, 1, 1, 3, 3);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    Invoke(form, "RefreshDatasetAssignments");
                    Assert.AreEqual(1, form.DatasetAssignments.Rows.Single().AnnotationCount);
                    ImageListRow beforeUndo = form.ImageBrowser.Rows.Single();
                    Assert.AreEqual(1, beforeUndo.AnnotationCount);
                    Assert.IsTrue(beforeUndo.ShowAnnotationCount);
                    CollectionAssert.AreEqual(new[] { "class1" }, beforeUndo.ClassNames.ToArray());
                    Invoke(form, "UndoOrRedoSelectedAnnotationSet", false);
                    Assert.AreEqual(0, form.DatasetAssignments.Rows.Single().AnnotationCount);
                    Assert.AreEqual(0, form.ImageBrowser.Rows.Single().AnnotationCount);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Cla_edits_and_clear_refresh_image_browser_classes_immediately()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string source = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(source, Color.Red);
                store = ProjectStore.Create(path);
                ProjectImage image = store.ImportFiles(new[] { source }).Single();
                AnnotationSet set = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                AnnotationClass other = store.AddClass(set.Id, "other", "#123456");
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, null));
                    FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
                    Click(classes.Controls.OfType<Button>().Single(button => button.Text == "class1"));
                    CollectionAssert.AreEqual(new[] { "class1" }, form.ImageBrowser.Rows.Single().ClassNames.ToArray());
                    Click(classes.Controls.OfType<Button>().Single(button => button.Text == other.Name));
                    CollectionAssert.AreEqual(new[] { other.Name }, form.ImageBrowser.Rows.Single().ClassNames.ToArray());
                    Invoke(form, "ClearDatasetClasses", (IReadOnlyList<string>)new[] { image.Id });
                    Assert.AreEqual(0, form.ImageBrowser.Rows.Single().ClassNames.Count);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Dataset_page_annotation_set_selector_stays_synchronized_and_refreshes_rows()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string source = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(source, Color.Red);
                store = ProjectStore.Create(path);
                ProjectImage image = store.ImportFiles(new[] { source }).Single();
                AnnotationSet annotated = store.CreateAnnotationSet("annotated", AnnotationSetType.Obd);
                AnnotationSet empty = store.CreateAnnotationSet("empty", AnnotationSetType.Obd);
                store.AddObdAnnotation(annotated.Id, image.Id, store.GetClasses(annotated.Id).Single().Id, 1, 1, 3, 3);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    ComboBox annotationSelector = Field<ComboBox>(form, "annotationSets");
                    ComboBox datasetSelector = Field<ComboBox>(form, "datasetAnnotationSets");
                    form.Size = new Size(1366, 768);
                    form.Show();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 2;
                    System.Windows.Forms.Application.DoEvents();
                    Assert.IsTrue(datasetSelector.Visible);
                    Assert.IsTrue(datasetSelector.Bounds.Width > 0 && datasetSelector.Bounds.Height > 0);

                    annotationSelector.SelectedItem = AnnotationSet(annotationSelector, empty.Id);
                    Assert.AreEqual(empty.Id, ((AnnotationSet)datasetSelector.SelectedItem).Id);
                    Assert.AreEqual(0, form.DatasetAssignments.Rows.Single().AnnotationCount);

                    SetField(form.AnnotationCanvas, "segLineStart", new PointF(2, 2));
                    datasetSelector.SelectedItem = AnnotationSet(datasetSelector, annotated.Id);
                    Assert.AreEqual(annotated.Id, ((AnnotationSet)annotationSelector.SelectedItem).Id);
                    Assert.AreEqual(1, form.DatasetAssignments.Rows.Single().AnnotationCount);
                    Assert.IsNull(Field<object>(form.AnnotationCanvas, "segLineStart"));
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void CreateSolidImage(string path, Color color)
        {
            using (var bitmap = new Bitmap(10, 10))
            {
                bitmap.SetPixel(0, 0, color);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            method.Invoke(target, arguments);
        }

        private static T Field<T>(object target, string fieldName) where T : class
        {
            return (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static AnnotationSet AnnotationSet(ComboBox values, string id)
        {
            return values.Items.Cast<AnnotationSet>().Single(value => value.Id == id);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void Click(Button button)
        {
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
        }
    }
}
