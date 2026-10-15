using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    internal static class JsonAnnotationExport
    {
        public static string Write(ProjectStore store, AnnotationSet set, string outputPath)
        {
            ProjectImage[] images = store.GetImages().Where(value => !value.IsMissing).ToArray();
            if (images.GroupBy(value => value.OriginalFileName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) throw new InvalidOperationException("Available images have duplicate original file names.");
            string path = UniquePath(outputPath);
            var classes = store.GetClasses(set.Id).ToDictionary(value => value.Id);
            var assignments = store.GetDatasetAssignments(set.Id).ToDictionary(value => value.ImageId);
            var cla = set.Type == AnnotationSetType.Cla ? store.GetClaClassIds(set.Id) : null;
            var obd = set.Type == AnnotationSetType.Obd ? store.GetObdAnnotations(set.Id).GroupBy(value => value.ImageId).ToDictionary(group => group.Key, group => group.ToArray()) : null;
            var seg = set.Type == AnnotationSetType.Seg ? store.GetSegRegions(set.Id).GroupBy(value => value.ImageId).ToDictionary(group => group.Key, group => group.ToArray()) : null;
            var data = new JArray();
            foreach (ProjectImage image in images)
            {
                var item = new JObject { ["fileName"] = image.OriginalFileName, ["set"] = SetName(assignments[image.Id].Status), ["classLabel"] = "", ["regionLabel"] = new JArray(), ["retestset"] = assignments[image.Id].Retest ? 1 : 0, ["rotation_angle"] = 0, ["width"] = image.Width, ["height"] = image.Height };
                if (set.Type == AnnotationSetType.Cla)
                {
                    string classId = cla.ContainsKey(image.Id) ? cla[image.Id] : null;
                    if (classId != null) item["classLabel"] = classes[classId].Name;
                }
                else if (set.Type == AnnotationSetType.Obd)
                    item["regionLabel"] = new JArray((obd.ContainsKey(image.Id) ? obd[image.Id] : new ObdAnnotation[0]).Select(value => new JObject { ["className"] = classes[value.ClassId].Name, ["type"] = "Rect", ["x"] = value.X, ["y"] = value.Y, ["width"] = value.Width, ["height"] = value.Height }));
                else item["regionLabel"] = new JArray((seg.ContainsKey(image.Id) ? seg[image.Id] : new SegRegion[0]).Select(value => Region(value, classes[value.ClassId].Name)));
                data.Add(item);
            }
            var root = new JObject { ["label_type"] = set.Type == AnnotationSetType.Cla ? "cla" : set.Type == AnnotationSetType.Obd ? "obd" : "seg", ["source"] = "labelset", ["time"] = DateTime.UtcNow.ToString("o"), ["version"] = "4.5.2.2", ["classes"] = new JArray(classes.Values.Select(value => new JObject { ["name"] = value.Name, ["color"] = AnnotationColorFormat.Normalize(value.Color) })), ["data"] = data };
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, root.ToString(Formatting.None), new System.Text.UTF8Encoding(false));
                File.Move(temporary, path);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static JObject Region(SegRegion value, string name)
        {
            var result = new JObject { ["className"] = name, ["type"] = value.Type == SegRegionType.Rect ? "Rect" : value.Type == SegRegionType.Ellipse ? "Ellipse" : value.Type == SegRegionType.PolyLine ? "PolyLine" : "PolyGon" };
            if (value.Type == SegRegionType.Rect) { result["x"] = value.X; result["y"] = value.Y; result["width"] = value.Width; result["height"] = value.Height; }
            else if (value.Type == SegRegionType.Ellipse) { result["x"] = value.X; result["y"] = value.Y; result["radiusx"] = value.Width; result["radiusy"] = value.Height; }
            else { if (value.Type == SegRegionType.PolyLine) result["strokeWidth"] = value.StrokeWidth; result["points"] = new JArray(value.Points.Select(point => new JArray(point.X, point.Y))); }
            return result;
        }

        private static string SetName(DatasetAssignmentStatus value) { return value == DatasetAssignmentStatus.Train ? "train" : value == DatasetAssignmentStatus.Test ? "test" : value == DatasetAssignmentStatus.NotUsed ? "not_used" : ""; }
        private static string UniquePath(string value) { string path = Path.GetFullPath(value); if (!String.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) path += ".json"; if (!File.Exists(path) && !Directory.Exists(path)) return path; string d = Path.GetDirectoryName(path), n = Path.GetFileNameWithoutExtension(path), e = Path.GetExtension(path); for (int i = 1; ; i++) { string candidate = Path.Combine(d, n + " (" + i + ")" + e); if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate; } }
    }
}
