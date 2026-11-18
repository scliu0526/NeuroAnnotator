using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace Neurocle.Application.Projects
{
    // 將標註顏色輸入正規化為穩定的 RGBA 文字與 System.Drawing 顏色。
    public static class AnnotationColorFormat
    {
        // 解析接受的 rgba(...) 色彩文字格式。
        private static readonly Regex Rgba = new Regex(@"^rgba\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d+(?:\.\d+)?)\s*\)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// 驗證並轉換色彩字串，保留自訂色彩標記且將透明度正規化為 1。
        /// </summary>
        /// <param name="value">rgba(...) 或 #RRGGBB 色彩字串，可帶 -custom 後綴。</param>
        /// <returns>正規化後的 rgba(R, G, B, 1) 字串。</returns>
        /// <exception cref="ArgumentException">輸入為空白或不是支援的色彩格式。</exception>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Color is required.", nameof(value));
            bool custom = value.EndsWith("-custom", StringComparison.Ordinal);
            string source = custom ? value.Substring(0, value.Length - 7) : value;
            Match match = Rgba.Match(source);
            Color color;
            if (match.Success)
            {
                int red = int.Parse(match.Groups[1].Value), green = int.Parse(match.Groups[2].Value), blue = int.Parse(match.Groups[3].Value);
                double alpha = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                if (red > 255 || green > 255 || blue > 255 || alpha < 0 || alpha > 1) throw new ArgumentException("Color is invalid.", nameof(value));
                color = Color.FromArgb(red, green, blue);
            }
            else
            {
                if (!Regex.IsMatch(source, @"^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Color is invalid.", nameof(value));
                color = ColorTranslator.FromHtml(source);
            }
            return string.Format("rgba({0}, {1}, {2}, 1){3}", color.R, color.G, color.B, custom ? "-custom" : "");
        }

        /// <summary>
        /// 將支援的色彩字串轉換為不透明的 RGB 顏色。
        /// </summary>
        /// <param name="value">rgba(...) 或 #RRGGBB 色彩字串，可帶 -custom 後綴。</param>
        /// <returns>對應的 System.Drawing.Color。</returns>
        public static Color ToColor(string value)
        {
            Match match = Rgba.Match(Normalize(value).Replace("-custom", ""));
            return Color.FromArgb(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        }
    }
    // 匯入圖片時可回報的失敗原因。
    public enum ImageImportFailure
    {
        // 檔案格式不受支援。
        UnsupportedFormat,
        // 檔案大小超過匯入限制。
        FileSizeLimit,
        // 圖片尺寸超過匯入限制。
        DimensionLimit,
        // 專案已達圖片數量上限。
        ProjectImageLimit,
        // 指定的檔案不存在。
        FileNotFound,
        // 檔案內容不是可讀取的有效圖片。
        InvalidImage,
        // 指定的目錄不存在。
        DirectoryNotFound,
        // 無法存取指定目錄。
        DirectoryAccess,
        // 未分類的匯入錯誤。
        Unknown
    }

    // 標註集支援的標註型別。
    public enum AnnotationSetType
    {
        // 整張圖片的分類標註。
        Cla,
        // 軸對齊矩形物件標註。
        Obd,
        // 可包含多邊形或筆畫的分割區域標註。
        Seg
    }

    // 表示一份具名稱、識別碼及標註型別的標註集。
    public sealed class AnnotationSet
    {
        /// <summary>
        /// 建立標註集摘要。
        /// </summary>
        /// <param name="id">標註集的不可變識別碼。</param>
        /// <param name="name">使用者指定的標註集名稱。</param>
        /// <param name="type">標註集所使用的型別。</param>
        public AnnotationSet(string id, string name, AnnotationSetType type) { Id = id; Name = name; Type = type; }
        // 取得標註集識別碼。
        public string Id { get; }
        // 取得標註集名稱。
        public string Name { get; }
        // 取得標註集型別。
        public AnnotationSetType Type { get; }
        // 取得由大寫型別名稱與標註集名稱組成的顯示文字。
        public string DisplayName { get { return Type.ToString().ToUpperInvariant() + " " + Name; } }
    }

    // 表示標註集中的類別名稱與顯示顏色。
    public sealed class AnnotationClass
    {
        /// <summary>
        /// 建立類別摘要。
        /// </summary>
        /// <param name="id">類別識別碼。</param>
        /// <param name="annotationSetId">所屬標註集識別碼。</param>
        /// <param name="name">類別名稱。</param>
        /// <param name="color">類別顏色字串。</param>
        public AnnotationClass(string id, string annotationSetId, string name, string color) { Id = id; AnnotationSetId = annotationSetId; Name = name; Color = color; }
        // 取得類別識別碼。
        public string Id { get; }
        // 取得所屬標註集識別碼。
        public string AnnotationSetId { get; }
        // 取得類別名稱。
        public string Name { get; }
        // 取得類別顏色字串。
        public string Color { get; }
    }

    // 表示帶有類別、圖片位置與繪製順序的軸對齊矩形標註。
    public sealed class ObdAnnotation
    {
        /// <summary>
        /// 建立矩形標註摘要。
        /// </summary>
        /// <param name="id">標註識別碼。</param>
        /// <param name="annotationSetId">所屬標註集識別碼。</param>
        /// <param name="imageId">所屬圖片識別碼。</param>
        /// <param name="classId">標註類別識別碼。</param>
        /// <param name="x">矩形左上角的原圖 X 座標。</param>
        /// <param name="y">矩形左上角的原圖 Y 座標。</param>
        /// <param name="width">矩形寬度。</param>
        /// <param name="height">矩形高度。</param>
        /// <param name="zOrder">矩形在圖片中的繪製順序。</param>
        public ObdAnnotation(string id, string annotationSetId, string imageId, string classId, int x, int y, int width, int height, int zOrder)
        {
            Id = id; AnnotationSetId = annotationSetId; ImageId = imageId; ClassId = classId;
            X = x; Y = y; Width = width; Height = height; ZOrder = zOrder;
        }

        // 取得標註識別碼。
        public string Id { get; }
        // 取得所屬標註集識別碼。
        public string AnnotationSetId { get; }
        // 取得所屬圖片識別碼。
        public string ImageId { get; }
        // 取得標註類別識別碼。
        public string ClassId { get; }
        // 取得矩形左上角的原圖 X 座標。
        public int X { get; }
        // 取得矩形左上角的原圖 Y 座標。
        public int Y { get; }
        // 取得矩形寬度。
        public int Width { get; }
        // 取得矩形高度。
        public int Height { get; }
        // 取得此標註的繪製順序。
        public int ZOrder { get; }
    }

    // SEG 區域所使用的幾何表示種類。
    public enum SegRegionType
    {
        // 由位置及寬高表示的矩形區域。
        Rect,
        // 由中心點及半徑表示的橢圓區域。
        Ellipse,
        // 以開放折線及筆畫寬度表示的區域。
        PolyLine,
        // 以封閉多邊形路徑表示的區域。
        PolyGon
    }

    // 以整數原圖座標表示的 SEG 路徑點。
    public sealed class SegPoint
    {
        /// <summary>
        /// 建立路徑點。
        /// </summary>
        /// <param name="x">原圖 X 座標。</param>
        /// <param name="y">原圖 Y 座標。</param>
        public SegPoint(int x, int y) { X = x; Y = y; }
        // 取得原圖 X 座標。
        public int X { get; }
        // 取得原圖 Y 座標。
        public int Y { get; }
    }

    // 表示帶類別、幾何資料及繪製順序的 SEG 區域。
    public sealed class SegRegion
    {
        /// <summary>
        /// 建立 SEG 區域摘要；矩形以左上角和尺寸表示，橢圓以中心和半徑表示。
        /// </summary>
        /// <param name="id">區域識別碼。</param>
        /// <param name="annotationSetId">所屬標註集識別碼。</param>
        /// <param name="imageId">所屬圖片識別碼。</param>
        /// <param name="classId">區域類別識別碼。</param>
        /// <param name="type">區域幾何表示種類。</param>
        /// <param name="x">形狀位置或中心的原圖 X 座標。</param>
        /// <param name="y">形狀位置或中心的原圖 Y 座標。</param>
        /// <param name="width">形狀寬度或水平半徑。</param>
        /// <param name="height">形狀高度或垂直半徑。</param>
        /// <param name="zOrder">區域在圖片中的繪製順序。</param>
        /// <param name="points">折線或多邊形路徑點；未提供時為空集合。</param>
        /// <param name="strokeWidth">筆畫寬度；非筆畫幾何通常為零。</param>
        public SegRegion(string id, string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height, int zOrder, IReadOnlyList<SegPoint> points = null, int strokeWidth = 0)
        {
            Id = id; AnnotationSetId = annotationSetId; ImageId = imageId; ClassId = classId; Type = type;
            X = x; Y = y; Width = width; Height = height; ZOrder = zOrder; Points = points ?? new SegPoint[0]; StrokeWidth = strokeWidth;
        }

        // 取得區域識別碼。
        public string Id { get; }
        // 取得所屬標註集識別碼。
        public string AnnotationSetId { get; }
        // 取得所屬圖片識別碼。
        public string ImageId { get; }
        // 取得區域類別識別碼。
        public string ClassId { get; }
        // 取得區域幾何表示種類。
        public SegRegionType Type { get; }
        // 取得矩形左上角或橢圓中心的 X 座標。
        public int X { get; }
        // 取得矩形左上角或橢圓中心的 Y 座標。
        public int Y { get; }
        // 取得寬度或水平半徑。
        public int Width { get; }
        // 取得高度或垂直半徑。
        public int Height { get; }
        // 取得區域的繪製順序。
        public int ZOrder { get; }
        // 取得折線或多邊形所使用的路徑點。
        public IReadOnlyList<SegPoint> Points { get; }
        // 取得筆畫幾何的寬度。
        public int StrokeWidth { get; }
    }

    // 以可識別的失敗原因回報單張圖片匯入失敗。
    public sealed class ImageImportException : Exception
    {
        /// <summary>
        /// 建立圖片匯入失敗例外。
        /// </summary>
        /// <param name="failure">匯入失敗原因。</param>
        public ImageImportException(ImageImportFailure failure) : base(failure.ToString())
        {
            Failure = failure;
        }

        // 取得匯入失敗原因。
        public ImageImportFailure Failure { get; }
    }

    // 重新連結既有圖片時可回報的失敗原因。
    public enum ImageRelinkFailure
    {
        // 專案中找不到指定圖片。
        ImageNotFound,
        // 指定的來源檔案不存在。
        SourceNotFound,
        // 來源檔案內容雜湊與原圖片不符。
        ContentMismatch,
        // 圖片目前已可用，無需重新連結。
        ImageIsAvailable
    }

    // 以可識別的失敗原因回報圖片重新連結失敗。
    public sealed class ImageRelinkException : Exception
    {
        /// <summary>
        /// 建立圖片重新連結失敗例外。
        /// </summary>
        /// <param name="failure">重新連結失敗原因。</param>
        public ImageRelinkException(ImageRelinkFailure failure) : base(failure.ToString())
        {
            Failure = failure;
        }

        // 取得重新連結失敗原因。
        public ImageRelinkFailure Failure { get; }
    }

    // 表示一份可供檢視或還原的專案備份。
    public sealed class ProjectBackup
    {
        /// <summary>
        /// 建立備份摘要。
        /// </summary>
        /// <param name="path">備份檔案路徑。</param>
        /// <param name="createdUtc">備份建立時間，使用 UTC。</param>
        public ProjectBackup(string path, DateTime createdUtc)
        {
            Path = path;
            CreatedUtc = createdUtc;
        }

        // 取得備份檔案路徑。
        public string Path { get; }
        // 取得備份建立的 UTC 時間。
        public DateTime CreatedUtc { get; }
    }

    // 表示專案資料庫無法開啟，並附帶可供復原的備份資訊。
    public sealed class ProjectOpenException : Exception
    {
        /// <summary>
        /// 建立專案開啟失敗例外。
        /// </summary>
        /// <param name="projectPath">無法開啟的專案路徑。</param>
        /// <param name="backups">可供使用者選擇還原的備份。</param>
        /// <param name="innerException">資料庫開啟失敗的原始例外。</param>
        public ProjectOpenException(string projectPath, IReadOnlyList<ProjectBackup> backups, Exception innerException)
            : base("The project database could not be opened.", innerException)
        {
            ProjectPath = projectPath;
            Backups = backups;
        }

        // 取得無法開啟的專案路徑。
        public string ProjectPath { get; }
        // 取得開啟失敗時可用的備份清單。
        public IReadOnlyList<ProjectBackup> Backups { get; }
    }

    // 表示備份還原失敗，但原專案已重新開啟的結果。
    public sealed class ProjectRestoreException : Exception
    {
        /// <summary>
        /// 建立還原失敗但已恢復原專案工作階段的例外。
        /// </summary>
        /// <param name="recoveredSession">重新開啟的原專案工作階段。</param>
        /// <param name="innerException">備份還原失敗的原始例外。</param>
        public ProjectRestoreException(IProjectSession recoveredSession, Exception innerException)
            : base("The project could not be restored, but the original project was reopened.", innerException)
        {
            RecoveredSession = recoveredSession;
        }

        // 取得還原失敗後重新開啟的原專案工作階段。
        public IProjectSession RecoveredSession { get; }
    }

    // 指出單一圖片匯入失敗檔案及其原因。
    public sealed class ImageImportFailureDetail
    {
        /// <summary>
        /// 建立單一檔案的匯入失敗摘要。
        /// </summary>
        /// <param name="filePath">無法匯入的檔案路徑。</param>
        /// <param name="failure">該檔案的匯入失敗原因。</param>
        public ImageImportFailureDetail(string filePath, ImageImportFailure failure)
        {
            FilePath = filePath;
            Failure = failure;
        }

        // 取得失敗檔案路徑。
        public string FilePath { get; }
        // 取得失敗原因。
        public ImageImportFailure Failure { get; }
    }

    // 彙整圖片匯入成功、失敗及掃描取消後未處理的結果。
    public sealed class ImageImportReport
    {
        /// <summary>
        /// 建立圖片匯入報告。
        /// </summary>
        /// <param name="imported">成功加入專案的圖片。</param>
        /// <param name="failures">未能匯入的檔案及失敗原因。</param>
        /// <param name="unprocessedCount">取消時尚未處理的檔案數量。</param>
        /// <param name="wasCancelledDuringScan">是否在掃描目錄時取消。</param>
        public ImageImportReport(IReadOnlyList<ProjectImage> imported, IReadOnlyList<ImageImportFailureDetail> failures, int unprocessedCount, bool wasCancelledDuringScan = false)
        {
            Imported = imported;
            Failures = failures;
            UnprocessedCount = unprocessedCount;
            WasCancelledDuringScan = wasCancelledDuringScan;
        }

        // 取得成功匯入的圖片。
        public IReadOnlyList<ProjectImage> Imported { get; }
        // 取得各失敗檔案的原因。
        public IReadOnlyList<ImageImportFailureDetail> Failures { get; }
        // 取得取消後未處理的檔案數量。
        public int UnprocessedCount { get; }
        // 取得是否在目錄掃描階段取消。
        public bool WasCancelledDuringScan { get; }
    }

    // 表示可回報給匯入進度介面的累計處理數量。
    public sealed class ImageImportProgress
    {
        /// <summary>
        /// 建立圖片匯入進度快照。
        /// </summary>
        /// <param name="processedCount">已處理檔案數量。</param>
        /// <param name="totalCount">預計處理檔案總數。</param>
        /// <param name="successfulCount">成功匯入檔案數量。</param>
        /// <param name="failedCount">匯入失敗檔案數量。</param>
        public ImageImportProgress(int processedCount, int totalCount, int successfulCount, int failedCount)
        {
            ProcessedCount = processedCount;
            TotalCount = totalCount;
            SuccessfulCount = successfulCount;
            FailedCount = failedCount;
        }

        // 取得已處理檔案數量。
        public int ProcessedCount { get; }
        // 取得預計處理檔案總數。
        public int TotalCount { get; }
        // 取得成功匯入檔案數量。
        public int SuccessfulCount { get; }
        // 取得匯入失敗檔案數量。
        public int FailedCount { get; }
    }

    // 提供專案資料、圖片、標註、資料集狀態及操作歷史的工作階段契約。
    public interface IProjectSession : IDisposable
    {
        // 取得此工作階段所開啟的專案檔案路徑。
        string ProjectPath { get; }
        /// <summary>
        /// 將目前尚未提交的專案狀態寫入持久儲存。
        /// </summary>
        void Save();
        /// <summary>
        /// 將專案另存至新路徑並回傳新專案工作階段。
        /// </summary>
        /// <param name="targetPath">新專案檔案路徑。</param>
        /// <returns>指向另存後專案的工作階段。</returns>
        IProjectSession SaveAs(string targetPath);
        /// <summary>
        /// 匯入檔案集合，並回傳成功匯入的圖片。
        /// </summary>
        /// <param name="filePaths">欲匯入的圖片檔案路徑。</param>
        /// <returns>成功加入專案的圖片。</returns>
        IReadOnlyList<ProjectImage> ImportFiles(IEnumerable<string> filePaths);
        /// <summary>
        /// 匯入檔案並回報逐檔結果；取消旗標在檔案處理期間檢查。
        /// </summary>
        /// <param name="filePaths">欲匯入的圖片檔案路徑。</param>
        /// <param name="cancellationRequested">回傳 true 時取消尚未完成的匯入。</param>
        /// <returns>包含成功、失敗與未處理數量的匯入報告。</returns>
        ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested);
        /// <summary>
        /// 匯入檔案、回報逐檔結果，並於處理進度變更時通知呼叫端。
        /// </summary>
        /// <param name="filePaths">欲匯入的圖片檔案路徑。</param>
        /// <param name="cancellationRequested">回傳 true 時取消尚未完成的匯入。</param>
        /// <param name="progress">接收目前已處理數量的回呼。</param>
        /// <returns>包含成功、失敗與未處理數量的匯入報告。</returns>
        ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested, Action<ImageImportProgress> progress);
        /// <summary>
        /// 掃描指定目錄並匯入圖片，可選擇是否遞迴子目錄。
        /// </summary>
        /// <param name="directoryPath">待掃描目錄。</param>
        /// <param name="includeSubdirectories">是否包含子目錄。</param>
        /// <param name="cancellationRequested">回傳 true 時取消後續掃描或匯入。</param>
        /// <returns>包含成功、失敗與未處理數量的匯入報告。</returns>
        ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested);
        /// <summary>
        /// 掃描目錄並匯入圖片，於進度變更時通知呼叫端。
        /// </summary>
        /// <param name="directoryPath">待掃描目錄。</param>
        /// <param name="includeSubdirectories">是否包含子目錄。</param>
        /// <param name="cancellationRequested">回傳 true 時取消後續掃描或匯入。</param>
        /// <param name="progress">接收目前處理進度的回呼。</param>
        /// <returns>包含成功、失敗與未處理數量的匯入報告。</returns>
        ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested, Action<ImageImportProgress> progress);
        /// <summary>
        /// 依 JSON 匯入並建立標註集。
        /// </summary>
        /// <param name="json">待匯入的標註 JSON 內容。</param>
        /// <param name="annotationSetName">新標註集名稱。</param>
        /// <param name="allowUnknownFields">是否允許並忽略格式中未知欄位。</param>
        /// <returns>新建立的標註集摘要。</returns>
        AnnotationSet ImportAnnotationJson(string json, string annotationSetName, bool allowUnknownFields);
        /// <summary>依指定模式將 JSON 標註、類別及資料集狀態匯入目前標註集。</summary>
        AnnotationJsonImportResult ImportAnnotationJsonIntoSet(string json, string annotationSetId, bool importAnnotationsAndClasses, AnnotationImportMode annotationMode, bool importDataset, DatasetImportMode datasetMode, bool allowUnknownFields);
        /// <summary>
        /// 將指定標註集匯出至 JSON 檔案。
        /// </summary>
        /// <param name="annotationSetId">要匯出的標註集識別碼。</param>
        /// <param name="outputPath">輸出檔案路徑。</param>
        /// <returns>實際寫入的輸出路徑。</returns>
        string ExportAnnotationJson(string annotationSetId, string outputPath);
        /// <summary>
        /// 將選取圖片以 ZIP 封存檔匯出，不含標註 JSON。
        /// </summary>
        /// <param name="outputPath">ZIP 輸出檔案路徑。</param>
        /// <param name="imageIds">要匯出的圖片識別碼。</param>
        /// <returns>實際寫入的 ZIP 路徑。</returns>
        string ExportImagesZip(string outputPath, IEnumerable<string> imageIds);
        /// <summary>
        /// 取得專案中的全部圖片，包含目前檔案缺失的項目。
        /// </summary>
        /// <returns>依專案順序排列的圖片資料。</returns>
        IReadOnlyList<ProjectImage> GetImages();
        /// <summary>
        /// 取得目前缺少受管理圖片檔案的項目。
        /// </summary>
        /// <returns>缺失圖片清單。</returns>
        IReadOnlyList<ProjectImage> GetMissingImages();
        /// <summary>
        /// 以內容相同的來源檔案重新連結缺失圖片。
        /// </summary>
        /// <param name="imageId">要重新連結的圖片識別碼。</param>
        /// <param name="sourcePath">候選來源檔案路徑。</param>
        void RelinkImage(string imageId, string sourcePath);
        /// <summary>
        /// 永久刪除指定圖片及專案中依附於圖片的資料。
        /// </summary>
        /// <param name="imageIds">要刪除的圖片識別碼。</param>
        void DeleteImages(IEnumerable<string> imageIds);
        /// <summary>
        /// 取得可用專案備份清單。
        /// </summary>
        /// <returns>依建立時間排列的備份摘要。</returns>
        IReadOnlyList<ProjectBackup> GetBackups();
        /// <summary>
        /// 以指定備份取代目前專案並回傳還原後的工作階段。
        /// </summary>
        /// <param name="backupPath">備份檔案路徑。</param>
        /// <returns>還原後專案的工作階段。</returns>
        IProjectSession RestoreBackup(string backupPath);
        /// <summary>
        /// 建立指定型別與名稱的標註集。
        /// </summary>
        /// <param name="name">新標註集名稱。</param>
        /// <param name="type">新標註集型別。</param>
        /// <returns>新建立的標註集。</returns>
        AnnotationSet CreateAnnotationSet(string name, AnnotationSetType type);
        /// <summary>
        /// 取得專案中的標註集清單。
        /// </summary>
        /// <returns>標註集摘要清單。</returns>
        IReadOnlyList<AnnotationSet> GetAnnotationSets();
        /// <summary>
        /// 變更指定標註集的名稱。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="name">新的標註集名稱。</param>
        void RenameAnnotationSet(string annotationSetId, string name);
        /// <summary>
        /// 只有確認名稱符合目前名稱時才永久刪除標註集及其資料。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="confirmationName">使用者輸入的確認名稱。</param>
        /// <returns>確認成功並刪除時回傳 true。</returns>
        bool DeleteAnnotationSet(string annotationSetId, string confirmationName);
        /// <summary>
        /// 取得指定標註集的全部類別。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>類別清單。</returns>
        IReadOnlyList<AnnotationClass> GetClasses(string annotationSetId);
        /// <summary>
        /// 在標註集中新增類別。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="name">新類別名稱。</param>
        /// <param name="color">新類別顏色。</param>
        /// <returns>新建立的類別。</returns>
        AnnotationClass AddClass(string annotationSetId, string name, string color);
        /// <summary>
        /// 變更指定類別的名稱。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="name">新的類別名稱。</param>
        void RenameClass(string classId, string name);
        /// <summary>
        /// 變更指定類別的顯示顏色。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="color">新的顏色字串。</param>
        void ChangeClassColor(string classId, string color);
        /// <summary>
        /// 刪除指定類別及其標註；每個標註集必須至少保留一個類別。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <exception cref="InvalidOperationException">刪除後會使標註集沒有任何類別。</exception>
        void DeleteClass(string classId);
        /// <summary>
        /// 設定指定圖片在 CLA 標註集中的分類類別。
        /// </summary>
        /// <param name="annotationSetId">CLA 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">類別識別碼；依實作契約可用 null 清除分類。</param>
        void SetClaClass(string annotationSetId, string imageId, string classId);
        /// <summary>
        /// 取得指定圖片目前的 CLA 類別識別碼。
        /// </summary>
        /// <param name="annotationSetId">CLA 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <returns>目前類別識別碼；尚未分類時為 null。</returns>
        string GetClaClassId(string annotationSetId, string imageId);
        /// <summary>
        /// 取得指定圖片的 OBD 矩形標註。
        /// </summary>
        /// <param name="annotationSetId">OBD 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <returns>依繪製順序排列的矩形標註。</returns>
        IReadOnlyList<ObdAnnotation> GetObdAnnotations(string annotationSetId, string imageId);
        /// <summary>
        /// 在圖片上新增 OBD 矩形標註。
        /// </summary>
        /// <param name="annotationSetId">OBD 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="x">矩形左上角 X 座標。</param>
        /// <param name="y">矩形左上角 Y 座標。</param>
        /// <param name="width">矩形寬度。</param>
        /// <param name="height">矩形高度。</param>
        /// <returns>新建立的 OBD 標註。</returns>
        ObdAnnotation AddObdAnnotation(string annotationSetId, string imageId, string classId, int x, int y, int width, int height);
        /// <summary>
        /// 將 OBD 標註複製至多張圖片，並回傳建立的複本。
        /// </summary>
        /// <param name="annotationSetId">OBD 標註集識別碼。</param>
        /// <param name="imageIds">複製目標圖片識別碼。</param>
        /// <param name="annotations">作為複製來源的標註。</param>
        /// <returns>新建立的標註複本。</returns>
        IReadOnlyList<ObdAnnotation> AddObdAnnotationCopies(string annotationSetId, IEnumerable<string> imageIds, IEnumerable<ObdAnnotation> annotations);
        /// <summary>
        /// 更新單一 OBD 標註的類別及矩形位置。
        /// </summary>
        /// <param name="annotationId">標註識別碼。</param>
        /// <param name="classId">新的類別識別碼。</param>
        /// <param name="x">矩形左上角 X 座標。</param>
        /// <param name="y">矩形左上角 Y 座標。</param>
        /// <param name="width">矩形寬度。</param>
        /// <param name="height">矩形高度。</param>
        void UpdateObdAnnotation(string annotationId, string classId, int x, int y, int width, int height);
        /// <summary>
        /// 批次更新多個 OBD 標註。
        /// </summary>
        /// <param name="annotations">包含新值的標註集合。</param>
        void UpdateObdAnnotations(IEnumerable<ObdAnnotation> annotations);
        /// <summary>
        /// 刪除單一 OBD 標註。
        /// </summary>
        /// <param name="annotationId">標註識別碼。</param>
        void DeleteObdAnnotation(string annotationId);
        /// <summary>
        /// 批次刪除多個 OBD 標註。
        /// </summary>
        /// <param name="annotationIds">欲刪除的標註識別碼。</param>
        void DeleteObdAnnotations(IEnumerable<string> annotationIds);
        /// <summary>
        /// 取得指定圖片的 SEG 區域。
        /// </summary>
        /// <param name="annotationSetId">SEG 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <returns>依繪製順序排列的 SEG 區域。</returns>
        IReadOnlyList<SegRegion> GetSegRegions(string annotationSetId, string imageId);
        /// <summary>
        /// 將 SEG 區域複製至多張圖片，並回傳建立的複本。
        /// </summary>
        /// <param name="annotationSetId">SEG 標註集識別碼。</param>
        /// <param name="imageIds">複製目標圖片識別碼。</param>
        /// <param name="regions">作為複製來源的區域。</param>
        /// <returns>新建立的區域複本。</returns>
        IReadOnlyList<SegRegion> AddSegRegionCopies(string annotationSetId, IEnumerable<string> imageIds, IEnumerable<SegRegion> regions);
        /// <summary>
        /// 以指定基本幾何在圖片上新增 SEG 區域。
        /// </summary>
        /// <param name="annotationSetId">SEG 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">區域類別識別碼。</param>
        /// <param name="type">區域幾何種類。</param>
        /// <param name="x">形狀位置或中心的 X 座標。</param>
        /// <param name="y">形狀位置或中心的 Y 座標。</param>
        /// <param name="width">寬度或水平半徑。</param>
        /// <param name="height">高度或垂直半徑。</param>
        /// <returns>新建立的 SEG 區域。</returns>
        SegRegion AddSegRegion(string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height);
        /// <summary>
        /// 依路徑點與筆畫寬度新增 SEG 路徑區域。
        /// </summary>
        /// <param name="annotationSetId">SEG 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">區域類別識別碼。</param>
        /// <param name="type">路徑幾何種類。</param>
        /// <param name="points">原圖座標路徑點。</param>
        /// <param name="strokeWidth">筆畫寬度。</param>
        /// <returns>新建立的 SEG 區域。</returns>
        SegRegion AddSegPath(string annotationSetId, string imageId, string classId, SegRegionType type, IReadOnlyList<SegPoint> points, int strokeWidth);
        /// <summary>
        /// 依指定筆畫從同類別 SEG 區域中擦除覆蓋範圍。
        /// </summary>
        /// <param name="annotationSetId">SEG 標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">要擦除的區域類別識別碼。</param>
        /// <param name="points">橡皮擦路徑點。</param>
        /// <param name="strokeWidth">橡皮擦筆畫寬度。</param>
        void EraseSegPath(string annotationSetId, string imageId, string classId, IReadOnlyList<SegPoint> points, int strokeWidth);
        /// <summary>
        /// 依指定 SEG 幾何從目前類別區域中擦除覆蓋範圍。
        /// </summary>
        void EraseSegGeometry(string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height, IReadOnlyList<SegPoint> points, int strokeWidth);
        /// <summary>
        /// 批次更新 SEG 區域的幾何及類別資料。
        /// </summary>
        /// <param name="regions">包含更新值的區域集合。</param>
        void UpdateSegRegions(IEnumerable<SegRegion> regions);
        /// <summary>
        /// 保存由整體旋轉或縮放產生的 SEG 幾何；保留仍合法的橋接，必要時依既有規則重建。
        /// </summary>
        /// <param name="regions">包含整體變換結果的區域集合。</param>
        IReadOnlyList<SegRegion> TransformSegRegions(IEnumerable<SegRegion> regions);
        /// <summary>
        /// 以共同整數位移移動指定 SEG 區域，並保留既有點序與橋接。
        /// </summary>
        /// <param name="regionIds">要共同移動的 SEG 區域識別碼。</param>
        /// <param name="offsetX">水平整數位移。</param>
        /// <param name="offsetY">垂直整數位移。</param>
        void TranslateSegRegions(IEnumerable<string> regionIds, int offsetX, int offsetY);
        /// <summary>
        /// 批次刪除 SEG 區域。
        /// </summary>
        /// <param name="regionIds">欲刪除的區域識別碼。</param>
        void DeleteSegRegions(IEnumerable<string> regionIds);
        /// <summary>
        /// 取得指定標註集在所有圖片上的資料集分配與 Retest 狀態。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>圖片分配狀態清單。</returns>
        IReadOnlyList<DatasetAssignment> GetDatasetAssignments(string annotationSetId);
        /// <summary>
        /// 取得指定標註集各圖片的類別與標註數量摘要。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>每張圖片的標註摘要。</returns>
        IReadOnlyList<ImageAnnotationSummary> GetImageAnnotationSummaries(string annotationSetId);
        /// <summary>
        /// 將多張圖片設為指定資料集狀態。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageIds">要更新狀態的圖片識別碼。</param>
        /// <param name="status">目標資料集狀態。</param>
        void SetDatasetAssignments(string annotationSetId, IEnumerable<string> imageIds, DatasetAssignmentStatus status);
        /// <summary>
        /// 設定多張圖片是否為 Retest。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageIds">要更新旗標的圖片識別碼。</param>
        /// <param name="retest">要設定的 Retest 值。</param>
        void SetRetest(string annotationSetId, IEnumerable<string> imageIds, bool retest);
        /// <summary>
        /// 清除指定圖片在標註集中的標註資料。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageIds">要清除標註的圖片識別碼。</param>
        void ClearAnnotations(string annotationSetId, IEnumerable<string> imageIds);
        /// <summary>
        /// 依標註類別分布套用訓練集與測試集分配，保留 Retest 旗標並回傳規劃警告。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="trainPercent">目標訓練集百分比。</param>
        /// <returns>實際產生的切分結果及警告。</returns>
        DatasetSplitPlan AutoSplitDataset(string annotationSetId, int trainPercent);
        /// <summary>
        /// 依指定圖片範圍自動分配資料集。
        /// </summary>
        DatasetSplitPlan AutoSplitDataset(string annotationSetId, int trainPercent, IReadOnlyCollection<string> imageIds);
        /// <summary>
        /// 復原標註集最近一次可復原的已完成操作。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>成功復原時回傳 true。</returns>
        bool Undo(string annotationSetId);
        /// <summary>
        /// 重做標註集最近一次已復原的操作。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>成功重做時回傳 true。</returns>
        bool Redo(string annotationSetId);
        /// <summary>
        /// 指出標註集目前是否有可復原的操作。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>有可復原操作時回傳 true。</returns>
        bool CanUndo(string annotationSetId);
        /// <summary>
        /// 指出標註集目前是否有可重做的操作。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>有可重做操作時回傳 true。</returns>
        bool CanRedo(string annotationSetId);
    }

    // JSON 標註與類別寫入目前標註集的方式。
    public enum AnnotationImportMode
    {
        AddToEmptyImages,
        AddToAllImages,
        ReplaceMatchingImages
    }

    // JSON 資料集狀態寫入目前標註集的方式。
    public enum DatasetImportMode
    {
        AddToUnassignedImages,
        ReplaceMatchingImages
    }

    public enum AnnotationJsonImportWarningReason
    {
        AmbiguousImageFileName,
        ImageDimensionsMismatch,
        InvalidAnnotation,
        UnknownClassReference,
        InvalidDatasetAssignment
    }

    public sealed class AnnotationJsonImportWarning
    {
        public AnnotationJsonImportWarning(string fileName, string sourceLocation, AnnotationJsonImportWarningReason reason)
        {
            FileName = fileName;
            SourceLocation = sourceLocation;
            Reason = reason;
        }

        public string FileName { get; }
        public string SourceLocation { get; }
        public AnnotationJsonImportWarningReason Reason { get; }
    }

    public sealed class AnnotationJsonImportResult
    {
        public AnnotationJsonImportResult(IEnumerable<AnnotationJsonImportWarning> warnings)
        {
            Warnings = Array.AsReadOnly((warnings ?? new AnnotationJsonImportWarning[0]).ToArray());
        }

        public IReadOnlyList<AnnotationJsonImportWarning> Warnings { get; }
    }

    // 圖片在單一標註集中的資料集分配狀態。
    public enum DatasetAssignmentStatus
    {
        // 尚未分配至資料集。
        Unassigned,
        // 分配至訓練集。
        Train,
        // 分配至測試／驗證集。
        Test,
        // 標記為不使用。
        NotUsed
    }

    // 表示圖片在指定標註集中的資料集狀態及 Retest 旗標。
    public sealed class DatasetAssignment
    {
        /// <summary>
        /// 建立圖片的資料集分配摘要。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="status">資料集分配狀態。</param>
        /// <param name="retest">圖片是否標記為 Retest。</param>
        public DatasetAssignment(string annotationSetId, string imageId, DatasetAssignmentStatus status, bool retest)
        {
            AnnotationSetId = annotationSetId;
            ImageId = imageId;
            Status = status;
            Retest = retest;
        }

        // 取得標註集識別碼。
        public string AnnotationSetId { get; }
        // 取得圖片識別碼。
        public string ImageId { get; }
        // 取得資料集分配狀態。
        public DatasetAssignmentStatus Status { get; }
        // 取得 Retest 旗標。
        public bool Retest { get; }
    }

    // 表示一張圖片在指定標註集中的類別與標註數量摘要。
    public sealed class ImageAnnotationSummary
    {
        /// <summary>
        /// 建立圖片標註摘要。
        /// </summary>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classIds">圖片上實際使用的類別識別碼。</param>
        /// <param name="annotationCount">圖片目前的標註數量。</param>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        public ImageAnnotationSummary(string imageId, IReadOnlyList<string> classIds, int annotationCount)
        {
            ImageId = imageId;
            ClassIds = classIds ?? throw new ArgumentNullException(nameof(classIds));
            AnnotationCount = annotationCount;
        }

        // 取得圖片識別碼。
        public string ImageId { get; private set; }
        // 取得圖片上實際使用的類別識別碼。
        public IReadOnlyList<string> ClassIds { get; private set; }
        // 取得圖片目前的標註數量。
        public int AnnotationCount { get; private set; }
    }

    // 表示專案中的受管理圖片及其來源、尺寸與可用狀態。
    public sealed class ProjectImage
    {
        /// <summary>
        /// 建立圖片資料摘要。
        /// </summary>
        /// <param name="id">圖片不可變識別碼。</param>
        /// <param name="originalFileName">匯入時來源檔名。</param>
        /// <param name="managedFileName">專案目前使用的受管理檔名。</param>
        /// <param name="sha256">來源內容的 SHA-256 雜湊值。</param>
        /// <param name="width">原圖寬度。</param>
        /// <param name="height">原圖高度。</param>
        /// <param name="sequence">目前顯示序號。</param>
        /// <param name="isMissing">受管理圖片檔案目前是否缺失。</param>
        public ProjectImage(string id, string originalFileName, string managedFileName, string sha256, int width, int height, int sequence, bool isMissing = false)
        {
            Id = id;
            OriginalFileName = originalFileName;
            ManagedFileName = managedFileName;
            Sha256 = sha256;
            Width = width;
            Height = height;
            Sequence = sequence;
            IsMissing = isMissing;
        }

        // 取得圖片不可變識別碼。
        public string Id { get; }
        // 取得匯入時來源檔名。
        public string OriginalFileName { get; }
        // 取得目前受管理圖片檔名。
        public string ManagedFileName { get; }
        // 取得來源內容的 SHA-256 雜湊值。
        public string Sha256 { get; }
        // 取得圖片寬度。
        public int Width { get; }
        // 取得圖片高度。
        public int Height { get; }
        // 取得目前顯示序號。
        public int Sequence { get; }
        // 取得受管理圖片檔案是否缺失。
        public bool IsMissing { get; }
    }

    // 建立、開啟或從備份還原專案的生命週期操作契約。
    public interface IProjectLifecycle
    {
        /// <summary>
        /// 建立新專案資料庫及受管理圖片目錄。
        /// </summary>
        /// <param name="projectPath">新專案檔案路徑。</param>
        /// <returns>新專案的工作階段。</returns>
        IProjectSession Create(string projectPath);
        /// <summary>
        /// 開啟既有專案並建立工作階段。
        /// </summary>
        /// <param name="projectPath">既有專案檔案路徑。</param>
        /// <returns>已開啟專案的工作階段。</returns>
        IProjectSession Open(string projectPath);
        /// <summary>
        /// 使用指定備份還原專案，並建立還原後的工作階段。
        /// </summary>
        /// <param name="projectPath">目標專案檔案路徑。</param>
        /// <param name="backupPath">備份檔案路徑。</param>
        /// <returns>還原後專案的工作階段。</returns>
        IProjectSession Restore(string projectPath, string backupPath);
    }
}
