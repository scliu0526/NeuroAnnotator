using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
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
                    Invoke(form, "UndoOrRedoSelectedAnnotationSet", false);
                    Assert.AreEqual(0, form.DatasetAssignments.Rows.Single().AnnotationCount);
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
    }
}
