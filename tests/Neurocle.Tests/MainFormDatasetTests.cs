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
using Neurocle.WinForms.Resources;

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
                    Field<TabControl>(form, "pages").SelectedTab = Field<TabPage>(form, "annotationPage");
                    Invoke(form, "RefreshDatasetAssignments");
                    Invoke(form, "RefreshImageBrowser");
                    Assert.AreEqual(1, form.DatasetAssignments.Rows.Single().AnnotationCount);
                    ImageListRow beforeUndo = form.ImageBrowser.Rows.Single();
                    Assert.AreEqual(1, beforeUndo.AnnotationCount);
                    Assert.IsTrue(beforeUndo.ShowAnnotationCount);
                    Assert.AreEqual("class1", string.Join(",", beforeUndo.ClassNames));
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
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Field<ListView>(form.ImageBrowser, "list").SelectedIndices.Add(0);
                    System.Windows.Forms.Application.DoEvents();
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
        public void Cla_batch_assignment_continues_after_failures_and_reports_copyable_results()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string[] sources = { Path.Combine(directory, "first.png"), Path.Combine(directory, "second.png"), Path.Combine(directory, "third.png") };
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                foreach (string source in sources) CreateSolidImage(source, Color.Red);
                store = ProjectStore.Create(path);
                ProjectImage[] images = store.ImportFiles(sources).ToArray();
                AnnotationSet set = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                AnnotationClass original = store.GetClasses(set.Id).Single();
                AnnotationClass target = store.AddClass(set.Id, "target", "#123456");
                AnnotationClass rejectedTarget = store.AddClass(set.Id, "rejected", "#654321");
                foreach (ProjectImage image in images) store.SetClaClass(set.Id, image.Id, original.Id);
                store.Save();
                store.Dispose();
                store = ProjectStore.Open(path);
                CreateClaAssignmentTrigger(store, "FailSecondClaAssignment", images[1].Id);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    ListView list = Field<ListView>(form.ImageBrowser, "list");
                    for (int index = 0; index < images.Length; index++) list.SelectedIndices.Add(index);
                    System.Windows.Forms.Application.DoEvents();
                    CollectionAssert.AreEquivalent(images.Select(image => image.Id).ToArray(), form.ImageBrowser.SelectedImageIds.ToArray());
                    Assert.AreEqual(images[0].Id, Field<string>(form, "selectedImageId"), "The first selected image must remain the current canvas image.");

                    string partialSummary = null;
                    string partialDetails = null;
                    string copiedDetails = null;
                    ClickClassAndInspectResult(form, target.Name, dialog =>
                    {
                        partialSummary = dialog.Controls.Find("claBatchSummary", true).OfType<Label>().Single().Text;
                        TextBox details = dialog.Controls.Find("failureDetails", true).OfType<TextBox>().Single();
                        partialDetails = details.Text;
                        Click(dialog.Controls.Find("copyFailures", true).OfType<Button>().Single());
                        copiedDetails = Clipboard.GetText();
                        Click(dialog.Controls.Find("close", true).OfType<Button>().Single());
                    });

                    StringAssert.Contains(partialSummary, "成功：2 張；失敗：1 張");
                    StringAssert.Contains(partialDetails, "second.png");
                    StringAssert.Contains(partialDetails, "injected CLA batch failure");
                    Assert.AreEqual(partialDetails, copiedDetails, "The failure details must be copyable.");
                    Assert.AreEqual(target.Id, store.GetClaClassId(set.Id, images[0].Id));
                    Assert.AreEqual(original.Id, store.GetClaClassId(set.Id, images[1].Id), "The failed image must keep its prior class.");
                    Assert.AreEqual(target.Id, store.GetClaClassId(set.Id, images[2].Id), "Processing must continue after the failed image.");
                    CollectionAssert.AreEquivalent(images.Select(image => image.Id).ToArray(), form.ImageBrowser.SelectedImageIds.ToArray());
                    CollectionAssert.AreEqual(new[] { target.Name }, form.ImageBrowser.Rows.Single(row => row.Image.Id == images[0].Id).ClassNames.ToArray());
                    Assert.IsFalse(store.CanUndo(set.Id));
                    Assert.IsFalse(store.CanRedo(set.Id));

                    DropClaAssignmentTrigger(store);
                    CreateClaAssignmentTrigger(store, "FailAllClaAssignments", null);
                    string failureSummary = null;
                    string failureDetails = null;
                    ClickClassAndInspectResult(form, rejectedTarget.Name, dialog =>
                    {
                        failureSummary = dialog.Controls.Find("claBatchSummary", true).OfType<Label>().Single().Text;
                        failureDetails = dialog.Controls.Find("failureDetails", true).OfType<TextBox>().Single().Text;
                        Click(dialog.Controls.Find("close", true).OfType<Button>().Single());
                    });
                    StringAssert.Contains(failureSummary, "成功：0 張；失敗：3 張");
                    foreach (ProjectImage image in images) StringAssert.Contains(failureDetails, image.OriginalFileName);
                    Assert.AreEqual(target.Id, store.GetClaClassId(set.Id, images[0].Id));
                    Assert.AreEqual(original.Id, store.GetClaClassId(set.Id, images[1].Id));
                    Assert.AreEqual(target.Id, store.GetClaClassId(set.Id, images[2].Id));

                    DropClaAssignmentTrigger(store);
                    ClickClass(form, target.Name);
                    Assert.AreEqual(0, System.Windows.Forms.Application.OpenForms.OfType<ClaBatchResultDialog>().Count(), "An all-success batch must not show a result summary.");
                    foreach (ProjectImage image in images) Assert.AreEqual(target.Id, store.GetClaClassId(set.Id, image.Id));
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

        [TestMethod]
        public void Delete_annotation_set_rejects_another_existing_set_name_and_reports_the_reason()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(path);
                AnnotationSet selected = store.CreateAnnotationSet("selected", AnnotationSetType.Cla);
                AnnotationSet other = store.CreateAnnotationSet("other", AnnotationSetType.Cla);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    string warning = null;
                    SetField(form, "promptText", new Func<string, string>(message => other.Name));
                    SetField(form, "showDeleteWarning", new Action<string>(message => warning = message));

                    Invoke(form, "DeleteSelectedAnnotationSet");

                    CollectionAssert.AreEquivalent(new[] { selected.Id, other.Id }, store.GetAnnotationSets().Select(value => value.Id).ToArray());
                    StringAssert.Contains(warning, selected.Name);
                    Assert.AreEqual(selected.Id, ((AnnotationSet)Field<ComboBox>(form, "annotationSets").SelectedItem).Id);

                    SetField(form, "promptText", new Func<string, string>(message => selected.Name));
                    Invoke(form, "DeleteSelectedAnnotationSet");
                    CollectionAssert.AreEqual(new[] { other.Id }, store.GetAnnotationSets().Select(value => value.Id).ToArray());
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Json_jobs_lock_only_their_target_annotation_set_for_editing()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            ImportJobQueue queue = null;
            var started = new System.Threading.ManualResetEventSlim();
            var release = new System.Threading.ManualResetEventSlim();
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(path);
                AnnotationSet set = store.CreateAnnotationSet("target", AnnotationSetType.Cla);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    queue = new ImportJobQueue((request, cancellationRequested, progress) =>
                    {
                        started.Set();
                        release.Wait(TimeSpan.FromSeconds(5));
                        return new ImportJobResult(false, new string[0]);
                    });
                    SetField(form, "importJobQueue", queue);
                    queue.Enqueue(new JsonExportJobRequest(set.Id, Path.Combine(directory, "target.json")));
                    Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
                    Assert.IsTrue((bool)Invoke(form, "IsSelectedAnnotationSetLocked"));
                    release.Set();
                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                    Assert.IsFalse((bool)Invoke(form, "IsSelectedAnnotationSetLocked"));
                }
                queue = null;
            }
            finally
            {
                release.Set();
                queue?.Dispose();
                store?.Dispose();
                started.Dispose();
                release.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Delete_selected_images_requires_confirmation_and_uses_only_explicit_selected_ids()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                string firstPath = Path.Combine(directory, "first.png");
                string secondPath = Path.Combine(directory, "second.png");
                string thirdPath = Path.Combine(directory, "third.png");
                CreateSolidImage(firstPath, Color.Red);
                CreateSolidImage(secondPath, Color.Green);
                CreateSolidImage(thirdPath, Color.Blue);
                store = ProjectStore.Create(path);
                ProjectImage[] images = store.ImportFiles(new[] { firstPath, secondPath, thirdPath }).ToArray();
                AnnotationSet cla = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                AnnotationSet obd = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                store.SetClaClass(cla.Id, images[0].Id, store.GetClasses(cla.Id).Single().Id);
                store.AddObdAnnotation(obd.Id, images[2].Id, store.GetClasses(obd.Id).Single().Id, 1, 1, 3, 3);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    ListView list = Field<ListView>(form.ImageBrowser, "list");
                    list.SelectedIndices.Add(0);
                    list.SelectedIndices.Add(2);
                    CollectionAssert.AreEqual(new[] { images[0].Id, images[2].Id }, form.ImageBrowser.SelectedImageIds.ToArray());
                    Button delete = form.Controls.Find("deleteSelectedImages", true).OfType<Button>().Single();
                    string prompt = null;
                    SetField(form, "confirmImageDeletion", new Func<string, bool>(message => { prompt = message; return false; }));

                    delete.PerformClick();
                    Assert.AreEqual(3, store.GetImages().Count);
                    StringAssert.Contains(prompt, "2");
                    StringAssert.Contains(prompt, "所有標註集");
                    StringAssert.Contains(prompt, "無法復原");

                    SetField(form, "confirmImageDeletion", new Func<string, bool>(message => true));
                    delete.PerformClick();
                    CollectionAssert.AreEqual(new[] { images[1].Id }, store.GetImages().Select(image => image.Id).ToArray());
                    Assert.AreEqual(1, store.GetImages().Single().Sequence);
                    Assert.IsNull(store.GetClaClassId(cla.Id, images[0].Id));
                    Assert.AreEqual(0, store.GetObdAnnotations(obd.Id, images[2].Id).Count);
                    Assert.AreEqual(1, form.ImageBrowser.Rows.Count);
                    Assert.IsNull(form.ImageCanvas.LoadedImagePath);
                    Assert.IsNull(form.AnnotationCanvas.LoadedImagePath);

                    SetField(form, "confirmImageDeletion", new Func<string, bool>(message => { Assert.Fail("Deletion confirmation must not appear without a selection."); return true; }));
                    delete.PerformClick();
                    Assert.AreEqual(1, store.GetImages().Count);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Delete_all_images_ignores_filter_and_selection_and_leaves_the_project_empty()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                string firstPath = Path.Combine(directory, "first.png");
                string secondPath = Path.Combine(directory, "second.png");
                string thirdPath = Path.Combine(directory, "third.png");
                CreateSolidImage(firstPath, Color.Red);
                CreateSolidImage(secondPath, Color.Green);
                CreateSolidImage(thirdPath, Color.Blue);
                store = ProjectStore.Create(path);
                ProjectImage[] images = store.ImportFiles(new[] { firstPath, secondPath, thirdPath }).ToArray();
                AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                store.AddObdAnnotation(set.Id, images[0].Id, store.GetClasses(set.Id).Single().Id, 1, 1, 3, 3);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    TextBox search = Field<TextBox>(form.ImageBrowser, "search");
                    search.Text = "second";
                    Field<ListView>(form.ImageBrowser, "list").SelectedIndices.Add(0);
                    CollectionAssert.AreEqual(new[] { images[1].Id }, form.ImageBrowser.SelectedImageIds.ToArray());
                    Button delete = form.Controls.Find("deleteAllImages", true).OfType<Button>().Single();
                    string prompt = null;
                    SetField(form, "confirmImageDeletion", new Func<string, bool>(message => { prompt = message; return false; }));

                    delete.PerformClick();
                    Assert.AreEqual(3, store.GetImages().Count);
                    StringAssert.Contains(prompt, "全部 3 張");
                    StringAssert.Contains(prompt, "永久刪除");
                    StringAssert.Contains(prompt, "所有標註集");
                    StringAssert.Contains(prompt, "無法復原");

                    SetField(form, "confirmImageDeletion", new Func<string, bool>(message => true));
                    delete.PerformClick();
                    Assert.AreEqual(0, store.GetImages().Count);
                    Assert.AreEqual(0, store.GetDatasetAssignments(set.Id).Count);
                    Assert.IsFalse(store.CanUndo(set.Id));
                    Assert.AreEqual(0, form.ImageBrowser.Rows.Count);
                    Assert.AreEqual(0, form.DatasetAssignments.Rows.Count);
                    Assert.IsNull(form.ImageCanvas.LoadedImagePath);
                    Assert.IsNull(form.AnnotationCanvas.LoadedImagePath);

                    SetField(form, "confirmImageDeletion", new Func<string, bool>(message => { Assert.Fail("Deletion confirmation must not appear for an empty project."); return true; }));
                    delete.PerformClick();
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void CreateClaAssignmentTrigger(ProjectStore store, string triggerName, string imageId)
        {
            store.CompleteOperation((connection, transaction) =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    string imageCondition = imageId == null ? string.Empty : " WHEN NEW.ImageId = '" + imageId.Replace("'", "''") + "'";
                    command.CommandText = "CREATE TRIGGER " + triggerName + " BEFORE INSERT ON ClaAnnotation" + imageCondition + " BEGIN SELECT RAISE(ABORT, 'injected CLA batch failure'); END;";
                    command.ExecuteNonQuery();
                }
            });
        }

        private static void DropClaAssignmentTrigger(ProjectStore store)
        {
            store.CompleteOperation((connection, transaction) =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DROP TRIGGER IF EXISTS FailSecondClaAssignment; DROP TRIGGER IF EXISTS FailAllClaAssignments;";
                    command.ExecuteNonQuery();
                }
            });
        }

        private static void ClickClass(MainForm form, string className)
        {
            FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
            Click(classes.Controls.OfType<Button>().Single(button => button.Text == className));
        }

        private static void ClickClassAndInspectResult(MainForm form, string className, Action<ClaBatchResultDialog> inspect)
        {
            Exception failure = null;
            bool foundDialog = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
            {
                timer.Tick += (sender, args) =>
                {
                    ClaBatchResultDialog dialog = System.Windows.Forms.Application.OpenForms.OfType<ClaBatchResultDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    foundDialog = true;
                    timer.Stop();
                    try { inspect(dialog); }
                    catch (Exception exception) { failure = exception; }
                    finally { dialog.Close(); }
                };
                timer.Start();
                ClickClass(form, className);
                timer.Stop();
            }
            Assert.IsTrue(foundDialog, "The CLA failure result dialog did not open.");
            if (failure != null) throw failure;
        }

        private static void CreateSolidImage(string path, Color color)
        {
            using (var bitmap = new Bitmap(10, 10))
            {
                bitmap.SetPixel(0, 0, color);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            return method.Invoke(target, arguments);
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
