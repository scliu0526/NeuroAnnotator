using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    internal sealed class JsonAnnotationImport
    {
        private static readonly HashSet<string> RootFields = new HashSet<string>(new[] { "label_type", "source", "time", "version", "classes", "data" }, StringComparer.Ordinal);

        public static ImportedAnnotationSet Parse(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new JsonAnnotationImportException("JSON is empty.");
            JObject root;
            try { root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
            catch (JsonException exception) { throw new JsonAnnotationImportException("JSON is invalid.", exception); }
            var unknownFields = new List<string>();
            CollectUnknown(root, RootFields, "root", unknownFields);
            string type = RequiredString(root, "label_type");
            AnnotationSetType annotationType;
            if (type == "cla") annotationType = AnnotationSetType.Cla;
            else if (type == "obd") annotationType = AnnotationSetType.Obd;
            else if (type == "seg") annotationType = AnnotationSetType.Seg;
            else throw new JsonAnnotationImportException("label_type is invalid.");
            if (RequiredString(root, "source") != "labelset" || RequiredString(root, "version") != "4.5.2.2") throw new JsonAnnotationImportException("JSON format is unsupported.");
            DateTime parsedTime;
            if (!DateTime.TryParse(RequiredDateText(root, "time"), null, System.Globalization.DateTimeStyles.RoundtripKind, out parsedTime)) throw new JsonAnnotationImportException("time is invalid.");
            JArray classes = RequiredArray(root, "classes");
            JArray data = RequiredArray(root, "data");
            if (classes.Count == 0) throw new JsonAnnotationImportException("classes is empty.");
            var importedClasses = classes.Select((value, index) => ParseClass(value, unknownFields, index)).ToArray();
            if (importedClasses.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != importedClasses.Length) throw new JsonAnnotationImportException("classes contains duplicates.");
            if (data.Any(value => !(value is JObject))) throw new JsonAnnotationImportException("data is invalid.");
            var classNames = new HashSet<string>(importedClasses.Select(value => value.Name), StringComparer.Ordinal);
            ImportedImage[] images = data.Cast<JObject>().Select((value, index) => ParseImage(value, annotationType, classNames, unknownFields, index)).ToArray();
            if (images.Select(value => value.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != images.Length) throw new JsonAnnotationImportException("data contains duplicate fileName values.");
            return new ImportedAnnotationSet(annotationType, importedClasses, images, unknownFields);
        }

        private static ImportedClass ParseClass(JToken token, ICollection<string> unknownFields, int index)
        {
            JObject value = token as JObject;
            if (value == null) throw new JsonAnnotationImportException("class is invalid.");
            CollectUnknown(value, new HashSet<string>(new[] { "name", "color" }, StringComparer.Ordinal), "classes[" + index + "]", unknownFields);
            return new ImportedClass(NormalizeLabelName(RequiredString(value, "name")), RequiredString(value, "color"));
        }

        private static ImportedImage ParseImage(JObject value, AnnotationSetType type, ISet<string> classNames, ICollection<string> unknownFields, int index)
        {
            CollectUnknown(value, new HashSet<string>(new[] { "fileName", "set", "classLabel", "regionLabel", "retestset", "rotation_angle", "width", "height" }, StringComparer.Ordinal), "data[" + index + "]", unknownFields);
            string fileName = RequiredString(value, "fileName");
            string set = RequiredStringOrEmpty(value, "set");
            if (set != "" && set != "train" && set != "test" && set != "not_used") throw new JsonAnnotationImportException("set is invalid.");
            string classLabel = RequiredStringOrEmpty(value, "classLabel");
            if (classLabel != "") classLabel = NormalizeLabelName(classLabel);
            int retest = RequiredInteger(value, "retestset");
            if (retest != 0 && retest != 1) throw new JsonAnnotationImportException("retestset is invalid.");
            if (RequiredInteger(value, "rotation_angle") != 0) throw new JsonAnnotationImportException("rotation_angle is invalid.");
            int width = RequiredInteger(value, "width"); int height = RequiredInteger(value, "height");
            if (width < 1 || height < 1) throw new JsonAnnotationImportException("image dimensions are invalid.");
            JArray regions = RequiredArray(value, "regionLabel");
            if (type == AnnotationSetType.Cla)
            {
                if (regions.Count != 0 || (classLabel != "" && !classNames.Contains(classLabel))) throw new JsonAnnotationImportException("CLA annotation is invalid.");
            }
            else if (classLabel != "") throw new JsonAnnotationImportException("classLabel is invalid for this annotation type.");
            ImportedRegion[] parsedRegions = regions.Select((region, regionIndex) => ParseRegion(region, type, classNames, width, height, unknownFields, index, regionIndex)).ToArray();
            return new ImportedImage(fileName, set, classLabel, retest == 1, width, height, parsedRegions);
        }

        private static ImportedRegion ParseRegion(JToken token, AnnotationSetType annotationType, ISet<string> classNames, int width, int height, ICollection<string> unknownFields, int imageIndex, int regionIndex)
        {
            JObject value = token as JObject;
            if (value == null) throw new JsonAnnotationImportException("region is invalid.");
            CollectUnknown(value, new HashSet<string>(new[] { "className", "type", "x", "y", "width", "height", "radiusx", "radiusy", "strokeWidth", "points" }, StringComparer.Ordinal), "data[" + imageIndex + "].regionLabel[" + regionIndex + "]", unknownFields);
            string className = NormalizeLabelName(RequiredString(value, "className"));
            if (!classNames.Contains(className)) throw new JsonAnnotationImportException("region className is unknown.");
            string name = RequiredString(value, "type");
            if (annotationType == AnnotationSetType.Obd && name != "Rect") throw new JsonAnnotationImportException("OBD region type is invalid.");
            SegRegionType type;
            if (name == "Rect") type = SegRegionType.Rect;
            else if (name == "Ellipse") type = SegRegionType.Ellipse;
            else if (name == "PolyLine") type = SegRegionType.PolyLine;
            else if (name == "PolyGon") type = SegRegionType.PolyGon;
            else throw new JsonAnnotationImportException("region type is unknown.");
            int x = OptionalInteger(value, "x"); int y = OptionalInteger(value, "y");
            int regionWidth = OptionalInteger(value, "width"); int regionHeight = OptionalInteger(value, "height");
            int radiusX = OptionalInteger(value, "radiusx"); int radiusY = OptionalInteger(value, "radiusy");
            int stroke = OptionalInteger(value, "strokeWidth");
            JArray points = value["points"] as JArray;
            if (type == SegRegionType.Rect && (regionWidth < 1 || regionHeight < 1 || x < 0 || y < 0 || (long)x + regionWidth > width || (long)y + regionHeight > height)) throw new JsonAnnotationImportException("rectangle is invalid.");
            if (type == SegRegionType.Ellipse && (radiusX < 1 || radiusY < 1 || x - radiusX < 0 || y - radiusY < 0 || (long)x + radiusX > width || (long)y + radiusY > height)) throw new JsonAnnotationImportException("ellipse is invalid.");
            if ((type == SegRegionType.PolyLine || type == SegRegionType.PolyGon) && (points == null || points.Count < (type == SegRegionType.PolyGon ? 3 : 2) || (type == SegRegionType.PolyLine && stroke < 1))) throw new JsonAnnotationImportException("path is invalid.");
            IReadOnlyList<SegPoint> parsedPoints = points == null ? new SegPoint[0] : ParsePoints(points, width, height, type);
            return new ImportedRegion(className, type, x, y, regionWidth, regionHeight, radiusX, radiusY, stroke, parsedPoints);
        }

        private static IReadOnlyList<SegPoint> ParsePoints(JArray values, int width, int height, SegRegionType type)
        {
            var result = new List<SegPoint>();
            foreach (JToken token in values)
            {
                JArray point = token as JArray;
                if (point == null || point.Count != 2 || !(point[0] is JValue) || !(point[1] is JValue) || point[0].Type != JTokenType.Integer || point[1].Type != JTokenType.Integer) throw new JsonAnnotationImportException("point is invalid.");
                int x = Convert.ToInt32(((JValue)point[0]).Value); int y = Convert.ToInt32(((JValue)point[1]).Value);
                if (x < 0 || y < 0 || x > width || y > height || (type != SegRegionType.PolyGon && (x == width || y == height))) throw new JsonAnnotationImportException("point is outside the image.");
                result.Add(new SegPoint(x, y));
            }
            return result;
        }

        private static void CollectUnknown(JObject value, ISet<string> allowed, string location, ICollection<string> unknownFields)
        {
            foreach (JProperty property in value.Properties().Where(property => !allowed.Contains(property.Name))) unknownFields.Add(location + "." + property.Name);
        }

        private static string RequiredString(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || token.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)token.Value)) throw new JsonAnnotationImportException(name + " is required.");
            return (string)token.Value;
        }

        private static string RequiredStringOrEmpty(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || token.Type != JTokenType.String) throw new JsonAnnotationImportException(name + " is required.");
            return (string)token.Value;
        }

        private static string NormalizeLabelName(string value)
        {
            string normalized = value.Trim();
            if (normalized.Length == 0) throw new JsonAnnotationImportException("class name is invalid.");
            return normalized;
        }

        private static string RequiredDateText(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || (token.Type != JTokenType.String && token.Type != JTokenType.Date)) throw new JsonAnnotationImportException(name + " is required.");
            return Convert.ToString(token.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int RequiredInteger(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || token.Type != JTokenType.Integer) throw new JsonAnnotationImportException(name + " is required.");
            try { return Convert.ToInt32(token.Value); } catch (OverflowException) { throw new JsonAnnotationImportException(name + " is invalid."); }
        }

        private static int OptionalInteger(JObject value, string name) { return value[name] == null ? 0 : RequiredInteger(value, name); }

        private static JArray RequiredArray(JObject value, string name)
        {
            JArray token = value[name] as JArray;
            if (token == null) throw new JsonAnnotationImportException(name + " is required.");
            return token;
        }
    }

    public sealed class JsonAnnotationImportException : Exception
    {
        public JsonAnnotationImportException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    internal sealed class ImportedAnnotationSet
    {
        public ImportedAnnotationSet(AnnotationSetType type, IReadOnlyList<ImportedClass> classes, IReadOnlyList<ImportedImage> data, IReadOnlyList<string> unknownFields) { Type = type; Classes = classes; Data = data; UnknownFields = unknownFields; }
        public AnnotationSetType Type { get; }
        public IReadOnlyList<ImportedClass> Classes { get; }
        public IReadOnlyList<ImportedImage> Data { get; }
        public IReadOnlyList<string> UnknownFields { get; }
    }

    internal sealed class ImportedClass
    {
        public ImportedClass(string name, string color) { Name = name; Color = color; }
        public string Name { get; }
        public string Color { get; }
    }

    internal sealed class ImportedImage
    {
        public ImportedImage(string fileName, string set, string classLabel, bool retest, int width, int height, IReadOnlyList<ImportedRegion> regions) { FileName = fileName; Set = set; ClassLabel = classLabel; Retest = retest; Width = width; Height = height; Regions = regions; }
        public string FileName { get; } public string Set { get; } public string ClassLabel { get; } public bool Retest { get; } public int Width { get; } public int Height { get; } public IReadOnlyList<ImportedRegion> Regions { get; }
    }

    internal sealed class ImportedRegion
    {
        public ImportedRegion(string className, SegRegionType type, int x, int y, int width, int height, int radiusX, int radiusY, int strokeWidth, IReadOnlyList<SegPoint> points) { ClassName = className; Type = type; X = x; Y = y; Width = width; Height = height; RadiusX = radiusX; RadiusY = radiusY; StrokeWidth = strokeWidth; Points = points; }
        public string ClassName { get; } public SegRegionType Type { get; } public int X { get; } public int Y { get; } public int Width { get; } public int Height { get; } public int RadiusX { get; } public int RadiusY { get; } public int StrokeWidth { get; } public IReadOnlyList<SegPoint> Points { get; }
    }
}
