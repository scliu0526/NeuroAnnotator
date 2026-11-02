using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    // 驗證交換 JSON 的版本、標註型別、欄位及幾何資料。
    internal sealed class JsonAnnotationImport
    {
        // 限定 JSON 根物件允許辨識的欄位名稱。
        private static readonly HashSet<string> RootFields = new HashSet<string>(new[] { "label_type", "source", "time", "version", "classes", "data" }, StringComparer.Ordinal);

        /// <summary>
        /// 剖析並驗證一份完整的標註集 JSON。
        /// </summary>
        /// <param name="json">待剖析及驗證的 JSON 文字。</param>
        /// <returns>回傳已完整驗證的標註集資料。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
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

        /// <summary>
        /// 驗證類別物件並建立正規化後的匯入類別。
        /// </summary>
        /// <param name="token">要剖析的 JSON 欄位或陣列項目。</param>
        /// <param name="unknownFields">要記錄的未知 JSON 欄位路徑集合。</param>
        /// <param name="index">集合或路徑中的索引。</param>
        /// <returns>回傳已驗證的類別資料。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static ImportedClass ParseClass(JToken token, ICollection<string> unknownFields, int index)
        {
            JObject value = token as JObject;
            if (value == null) throw new JsonAnnotationImportException("class is invalid.");
            CollectUnknown(value, new HashSet<string>(new[] { "name", "color" }, StringComparer.Ordinal), "classes[" + index + "]", unknownFields);
            return new ImportedClass(NormalizeLabelName(RequiredString(value, "name")), RequiredString(value, "color"));
        }

        /// <summary>
        /// 驗證圖片及其資料集狀態，並剖析所含標註區域。
        /// </summary>
        /// <param name="value">包含圖片欄位與標註區域的 JSON 物件。</param>
        /// <param name="type">標註集或標註區域的型別。</param>
        /// <param name="classNames">標註集中允許使用的類別名稱。</param>
        /// <param name="unknownFields">要記錄的未知 JSON 欄位路徑集合。</param>
        /// <param name="index">集合或路徑中的索引。</param>
        /// <returns>回傳已驗證的圖片與標註資料。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
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

        /// <summary>
        /// 依 OBD 或 SEG 型別驗證區域欄位、尺寸與類別關聯。
        /// </summary>
        /// <param name="token">要剖析的 JSON 欄位或陣列項目。</param>
        /// <param name="annotationType">匯入資料使用的標註型別。</param>
        /// <param name="classNames">標註集中允許使用的類別名稱。</param>
        /// <param name="width">圖片或形狀的寬度。</param>
        /// <param name="height">圖片或形狀的高度。</param>
        /// <param name="unknownFields">要記錄的未知 JSON 欄位路徑集合。</param>
        /// <param name="imageIndex">圖片在輸入集合中的索引。</param>
        /// <param name="regionIndex">區域在圖片中的索引。</param>
        /// <returns>回傳已驗證的標註區域資料。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
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
            if ((type == SegRegionType.PolyLine || type == SegRegionType.PolyGon) && (points == null || points.Count < (type == SegRegionType.PolyGon ? 3 : 1) || (type == SegRegionType.PolyLine && stroke < 1))) throw new JsonAnnotationImportException("path is invalid.");
            IReadOnlyList<SegPoint> parsedPoints = points == null ? new SegPoint[0] : ParsePoints(points, width, height, type);
            return new ImportedRegion(className, type, x, y, regionWidth, regionHeight, radiusX, radiusY, stroke, parsedPoints);
        }

        /// <summary>
        /// 驗證路徑點的整數格式及圖片座標範圍。
        /// </summary>
        /// <param name="values">要估算、驗證或處理的值集合。</param>
        /// <param name="width">圖片或形狀的寬度。</param>
        /// <param name="height">圖片或形狀的高度。</param>
        /// <param name="type">標註集或標註區域的型別。</param>
        /// <returns>回傳通過整數格式與圖片範圍驗證的點集合。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
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

        /// <summary>
        /// 記錄物件中不屬於允許欄位集合的欄位路徑。
        /// </summary>
        /// <param name="value">要檢查未知欄位的 JSON 物件。</param>
        /// <param name="allowed">允許欄位名稱的集合。</param>
        /// <param name="location">JSON 物件或陣列的路徑前綴，例如 <c>data[3]</c>。</param>
        /// <param name="unknownFields">要記錄的未知 JSON 欄位路徑集合。</param>
        private static void CollectUnknown(JObject value, ISet<string> allowed, string location, ICollection<string> unknownFields)
        {
            foreach (JProperty property in value.Properties().Where(property => !allowed.Contains(property.Name))) unknownFields.Add(location + "." + property.Name);
        }

        /// <summary>
        /// 取得非空白 JSON 字串欄位，欄位缺少或型別錯誤時拒絕匯入。
        /// </summary>
        /// <param name="value">包含待讀取欄位的 JSON 物件。</param>
        /// <param name="name">要從 JSON 物件讀取的字串欄位名稱。</param>
        /// <returns>回傳指定的非空白字串欄位。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static string RequiredString(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || token.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)token.Value)) throw new JsonAnnotationImportException(name + " is required.");
            return (string)token.Value;
        }

        /// <summary>
        /// 取得 JSON 字串欄位，允許其內容為空字串。
        /// </summary>
        /// <param name="value">包含待讀取欄位的 JSON 物件。</param>
        /// <param name="name">要從 JSON 物件讀取的字串欄位名稱。</param>
        /// <returns>回傳指定的字串欄位，允許空字串。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static string RequiredStringOrEmpty(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || token.Type != JTokenType.String) throw new JsonAnnotationImportException(name + " is required.");
            return (string)token.Value;
        }

        /// <summary>
        /// 修剪類別名稱並拒絕空白名稱。
        /// </summary>
        /// <param name="value">待修剪空白並驗證的類別名稱。</param>
        /// <returns>回傳修剪空白後且非空的類別名稱。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static string NormalizeLabelName(string value)
        {
            string normalized = value.Trim();
            if (normalized.Length == 0) throw new JsonAnnotationImportException("class name is invalid.");
            return normalized;
        }

        /// <summary>
        /// 取得字串或日期型別的時間欄位並轉成不變文化字串。
        /// </summary>
        /// <param name="value">包含待讀取時間欄位的 JSON 物件。</param>
        /// <param name="name">要從 JSON 物件讀取的時間欄位名稱。</param>
        /// <returns>回傳轉換為不變文化格式的時間文字。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static string RequiredDateText(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || (token.Type != JTokenType.String && token.Type != JTokenType.Date)) throw new JsonAnnotationImportException(name + " is required.");
            return Convert.ToString(token.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 取得整數欄位，並將超出 Int32 範圍的值轉成匯入錯誤。
        /// </summary>
        /// <param name="value">包含待讀取整數欄位的 JSON 物件。</param>
        /// <param name="name">要從 JSON 物件讀取的整數欄位名稱。</param>
        /// <returns>回傳指定欄位中的 Int32 整數。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static int RequiredInteger(JObject value, string name)
        {
            JValue token = value[name] as JValue;
            if (token == null || token.Type != JTokenType.Integer) throw new JsonAnnotationImportException(name + " is required.");
            try { return Convert.ToInt32(token.Value); } catch (OverflowException) { throw new JsonAnnotationImportException(name + " is invalid."); }
        }

        /// <summary>
        /// 讀取可省略的整數欄位，缺少時以零表示。
        /// </summary>
        /// <param name="value">包含待讀取欄位的 JSON 物件。</param>
        /// <param name="name">要從 JSON 物件讀取的整數欄位名稱。</param>
        /// <returns>欄位存在時回傳欄位的 Int32 整數；欄位缺少時回傳零。</returns>
        /// <exception cref="JsonAnnotationImportException">欄位存在但不是有效 Int32 整數時擲回。</exception>
        private static int OptionalInteger(JObject value, string name) { return value[name] == null ? 0 : RequiredInteger(value, name); }

        /// <summary>
        /// 取得 JSON 陣列欄位，欄位缺少或型別錯誤時拒絕匯入。
        /// </summary>
        /// <param name="value">包含待讀取陣列欄位的 JSON 物件。</param>
        /// <param name="name">要從 JSON 物件讀取的陣列欄位名稱。</param>
        /// <returns>回傳指定的 JSON 陣列欄位。</returns>
        /// <exception cref="JsonAnnotationImportException">JSON 內容不符合標註匯入格式。</exception>
        private static JArray RequiredArray(JObject value, string name)
        {
            JArray token = value[name] as JArray;
            if (token == null) throw new JsonAnnotationImportException(name + " is required.");
            return token;
        }
    }

    // 表示標註 JSON 格式或內容不符合匯入契約。
    public sealed class JsonAnnotationImportException : Exception
    {
        /// <summary>
        /// 建立匯入例外，並保留造成錯誤的原始例外。
        /// </summary>
        public JsonAnnotationImportException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    // 保存已驗證的標註集型別、類別、圖片及未知欄位資訊。
    internal sealed class ImportedAnnotationSet
    {
        /// <summary>
        /// 建立已剖析的標註集資料。
        /// </summary>
        public ImportedAnnotationSet(AnnotationSetType type, IReadOnlyList<ImportedClass> classes, IReadOnlyList<ImportedImage> data, IReadOnlyList<string> unknownFields) { Type = type; Classes = classes; Data = data; UnknownFields = unknownFields; }
        // 取得匯入標註集的型別。
        public AnnotationSetType Type { get; }
        // 取得已驗證的類別清單。
        public IReadOnlyList<ImportedClass> Classes { get; }
        // 取得已驗證的圖片資料。
        public IReadOnlyList<ImportedImage> Data { get; }
        // 取得來源 JSON 中未辨識欄位的路徑。
        public IReadOnlyList<string> UnknownFields { get; }
    }

    // 保存一筆已驗證的類別名稱與顏色。
    internal sealed class ImportedClass
    {
        /// <summary>
        /// 建立匯入類別資料。
        /// </summary>
        public ImportedClass(string name, string color) { Name = name; Color = color; }
        // 取得類別名稱。
        public string Name { get; }
        // 取得類別色彩字串。
        public string Color { get; }
    }

    // 保存一張已驗證圖片及其分類、資料集與區域標註資料。
    internal sealed class ImportedImage
    {
        /// <summary>
        /// 建立匯入圖片資料及其區域清單。
        /// </summary>
        public ImportedImage(string fileName, string set, string classLabel, bool retest, int width, int height, IReadOnlyList<ImportedRegion> regions) { FileName = fileName; Set = set; ClassLabel = classLabel; Retest = retest; Width = width; Height = height; Regions = regions; }
        // 取得匯入格式中的圖片檔名。
        public string FileName { get; }
        // 取得 train、test 或 not_used 資料集名稱。
        public string Set { get; }
        // 取得 CLA 圖片類別名稱。
        public string ClassLabel { get; }
        // 取得圖片的 Retest 旗標。
        public bool Retest { get; }
        // 取得圖片寬度。
        public int Width { get; }
        // 取得圖片高度。
        public int Height { get; }
        // 取得圖片內已驗證的標註區域。
        public IReadOnlyList<ImportedRegion> Regions { get; }
    }

    // 保存一筆已驗證的 OBD 或 SEG 區域幾何資料。
    internal sealed class ImportedRegion
    {
        /// <summary>
        /// 建立標註區域資料及其幾何座標。
        /// </summary>
        public ImportedRegion(string className, SegRegionType type, int x, int y, int width, int height, int radiusX, int radiusY, int strokeWidth, IReadOnlyList<SegPoint> points) { ClassName = className; Type = type; X = x; Y = y; Width = width; Height = height; RadiusX = radiusX; RadiusY = radiusY; StrokeWidth = strokeWidth; Points = points; }
        // 取得區域所屬的類別名稱。
        public string ClassName { get; }
        // 取得區域幾何種類。
        public SegRegionType Type { get; }
        // 取得區域左上角或橢圓中心的 X 座標。
        public int X { get; }
        // 取得區域左上角或橢圓中心的 Y 座標。
        public int Y { get; }
        // 取得矩形寬度或橢圓 X 半徑。
        public int Width { get; }
        // 取得矩形高度或橢圓 Y 半徑。
        public int Height { get; }
        // 取得橢圓 X 半徑欄位。
        public int RadiusX { get; }
        // 取得橢圓 Y 半徑欄位。
        public int RadiusY { get; }
        // 取得 PolyLine 的筆畫寬度。
        public int StrokeWidth { get; }
        // 取得多邊形或線段的點位清單。
        public IReadOnlyList<SegPoint> Points { get; }
    }
}
