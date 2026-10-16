using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Neurocle.Application.Projects
{
    public static class AnnotationColorFormat
    {
        private static readonly Regex Rgba = new Regex(@"^rgba\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d+(?:\.\d+)?)\s*\)$", RegexOptions.CultureInvariant);

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

        public static Color ToColor(string value)
        {
            Match match = Rgba.Match(Normalize(value).Replace("-custom", ""));
            return Color.FromArgb(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        }
    }
    public enum ImageImportFailure
    {
        UnsupportedFormat,
        FileSizeLimit,
        DimensionLimit,
        ProjectImageLimit,
        FileNotFound,
        InvalidImage,
        DirectoryNotFound,
        DirectoryAccess,
        Unknown
    }

    public enum AnnotationSetType { Cla, Obd, Seg }

    public sealed class AnnotationSet
    {
        public AnnotationSet(string id, string name, AnnotationSetType type) { Id = id; Name = name; Type = type; }
        public string Id { get; }
        public string Name { get; }
        public AnnotationSetType Type { get; }
        public string DisplayName { get { return Type.ToString().ToUpperInvariant() + " " + Name; } }
    }

    public sealed class AnnotationClass
    {
        public AnnotationClass(string id, string annotationSetId, string name, string color) { Id = id; AnnotationSetId = annotationSetId; Name = name; Color = color; }
        public string Id { get; }
        public string AnnotationSetId { get; }
        public string Name { get; }
        public string Color { get; }
    }

    public sealed class ObdAnnotation
    {
        public ObdAnnotation(string id, string annotationSetId, string imageId, string classId, int x, int y, int width, int height, int zOrder)
        {
            Id = id; AnnotationSetId = annotationSetId; ImageId = imageId; ClassId = classId;
            X = x; Y = y; Width = width; Height = height; ZOrder = zOrder;
        }

        public string Id { get; }
        public string AnnotationSetId { get; }
        public string ImageId { get; }
        public string ClassId { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int ZOrder { get; }
    }

    public enum SegRegionType { Rect, Ellipse, PolyLine, PolyGon }

    public sealed class SegPoint
    {
        public SegPoint(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    public sealed class SegRegion
    {
        // Rect uses X/Y as top-left and Width/Height as dimensions. Ellipse uses X/Y as centre and Width/Height as radii.
        public SegRegion(string id, string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height, int zOrder, IReadOnlyList<SegPoint> points = null, int strokeWidth = 0)
        {
            Id = id; AnnotationSetId = annotationSetId; ImageId = imageId; ClassId = classId; Type = type;
            X = x; Y = y; Width = width; Height = height; ZOrder = zOrder; Points = points ?? new SegPoint[0]; StrokeWidth = strokeWidth;
        }

        public string Id { get; }
        public string AnnotationSetId { get; }
        public string ImageId { get; }
        public string ClassId { get; }
        public SegRegionType Type { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int ZOrder { get; }
        public IReadOnlyList<SegPoint> Points { get; }
        public int StrokeWidth { get; }
    }

    public sealed class ImageImportException : Exception
    {
        public ImageImportException(ImageImportFailure failure) : base(failure.ToString())
        {
            Failure = failure;
        }

        public ImageImportFailure Failure { get; }
    }

    public enum ImageRelinkFailure
    {
        ImageNotFound,
        SourceNotFound,
        ContentMismatch,
        ImageIsAvailable
    }

    public sealed class ImageRelinkException : Exception
    {
        public ImageRelinkException(ImageRelinkFailure failure) : base(failure.ToString())
        {
            Failure = failure;
        }

        public ImageRelinkFailure Failure { get; }
    }

    public sealed class ProjectBackup
    {
        public ProjectBackup(string path, DateTime createdUtc)
        {
            Path = path;
            CreatedUtc = createdUtc;
        }

        public string Path { get; }
        public DateTime CreatedUtc { get; }
    }

    public sealed class ProjectOpenException : Exception
    {
        public ProjectOpenException(string projectPath, IReadOnlyList<ProjectBackup> backups, Exception innerException)
            : base("The project database could not be opened.", innerException)
        {
            ProjectPath = projectPath;
            Backups = backups;
        }

        public string ProjectPath { get; }
        public IReadOnlyList<ProjectBackup> Backups { get; }
    }

    public sealed class ProjectRestoreException : Exception
    {
        public ProjectRestoreException(IProjectSession recoveredSession, Exception innerException)
            : base("The project could not be restored, but the original project was reopened.", innerException)
        {
            RecoveredSession = recoveredSession;
        }

        public IProjectSession RecoveredSession { get; }
    }

    public sealed class ImageImportFailureDetail
    {
        public ImageImportFailureDetail(string filePath, ImageImportFailure failure)
        {
            FilePath = filePath;
            Failure = failure;
        }

        public string FilePath { get; }
        public ImageImportFailure Failure { get; }
    }

    public sealed class ImageImportReport
    {
        public ImageImportReport(IReadOnlyList<ProjectImage> imported, IReadOnlyList<ImageImportFailureDetail> failures, int unprocessedCount, bool wasCancelledDuringScan = false)
        {
            Imported = imported;
            Failures = failures;
            UnprocessedCount = unprocessedCount;
            WasCancelledDuringScan = wasCancelledDuringScan;
        }

        public IReadOnlyList<ProjectImage> Imported { get; }
        public IReadOnlyList<ImageImportFailureDetail> Failures { get; }
        public int UnprocessedCount { get; }
        public bool WasCancelledDuringScan { get; }
    }

    public sealed class ImageImportProgress
    {
        public ImageImportProgress(int processedCount, int totalCount, int successfulCount, int failedCount)
        {
            ProcessedCount = processedCount;
            TotalCount = totalCount;
            SuccessfulCount = successfulCount;
            FailedCount = failedCount;
        }

        public int ProcessedCount { get; }
        public int TotalCount { get; }
        public int SuccessfulCount { get; }
        public int FailedCount { get; }
    }
    public interface IProjectSession : IDisposable
    {
        string ProjectPath { get; }
        void Save();
        IProjectSession SaveAs(string targetPath);
        IReadOnlyList<ProjectImage> ImportFiles(IEnumerable<string> filePaths);
        ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested);
        ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested, Action<ImageImportProgress> progress);
        ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested);
        ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested, Action<ImageImportProgress> progress);
        AnnotationSet ImportAnnotationJson(string json, string annotationSetName, bool allowUnknownFields);
        string ExportAnnotationJson(string annotationSetId, string outputPath);
        string ExportImagesZip(string outputPath, IEnumerable<string> imageIds);
        IReadOnlyList<ProjectImage> GetImages();
        IReadOnlyList<ProjectImage> GetMissingImages();
        void RelinkImage(string imageId, string sourcePath);
        void DeleteImages(IEnumerable<string> imageIds);
        IReadOnlyList<ProjectBackup> GetBackups();
        IProjectSession RestoreBackup(string backupPath);
        AnnotationSet CreateAnnotationSet(string name, AnnotationSetType type);
        IReadOnlyList<AnnotationSet> GetAnnotationSets();
        void RenameAnnotationSet(string annotationSetId, string name);
        bool DeleteAnnotationSet(string annotationSetId, string confirmationName);
        IReadOnlyList<AnnotationClass> GetClasses(string annotationSetId);
        AnnotationClass AddClass(string annotationSetId, string name, string color);
        void RenameClass(string classId, string name);
        void ChangeClassColor(string classId, string color);
        void DeleteClass(string classId);
        void SetClaClass(string annotationSetId, string imageId, string classId);
        string GetClaClassId(string annotationSetId, string imageId);
        IReadOnlyList<ObdAnnotation> GetObdAnnotations(string annotationSetId, string imageId);
        ObdAnnotation AddObdAnnotation(string annotationSetId, string imageId, string classId, int x, int y, int width, int height);
        IReadOnlyList<ObdAnnotation> AddObdAnnotationCopies(string annotationSetId, string imageId, IEnumerable<ObdAnnotation> annotations);
        void UpdateObdAnnotation(string annotationId, string classId, int x, int y, int width, int height);
        void UpdateObdAnnotations(IEnumerable<ObdAnnotation> annotations);
        void DeleteObdAnnotation(string annotationId);
        void DeleteObdAnnotations(IEnumerable<string> annotationIds);
        IReadOnlyList<SegRegion> GetSegRegions(string annotationSetId, string imageId);
        SegRegion AddSegRegion(string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height);
        SegRegion AddSegPath(string annotationSetId, string imageId, string classId, SegRegionType type, IReadOnlyList<SegPoint> points, int strokeWidth);
        void EraseSegPath(string annotationSetId, string imageId, string classId, IReadOnlyList<SegPoint> points, int strokeWidth);
        void UpdateSegRegions(IEnumerable<SegRegion> regions);
        void DeleteSegRegions(IEnumerable<string> regionIds);
        IReadOnlyList<DatasetAssignment> GetDatasetAssignments(string annotationSetId);
        IReadOnlyList<ImageAnnotationSummary> GetImageAnnotationSummaries(string annotationSetId);
        void SetDatasetAssignments(string annotationSetId, IEnumerable<string> imageIds, DatasetAssignmentStatus status);
        void SetRetest(string annotationSetId, IEnumerable<string> imageIds, bool retest);
        void ClearAnnotations(string annotationSetId, IEnumerable<string> imageIds);
        DatasetSplitPlan AutoSplitDataset(string annotationSetId, int trainPercent);
        bool Undo(string annotationSetId);
        bool Redo(string annotationSetId);
        bool CanUndo(string annotationSetId);
        bool CanRedo(string annotationSetId);
    }

    public enum DatasetAssignmentStatus { Unassigned, Train, Test, NotUsed }

    public sealed class DatasetAssignment
    {
        public DatasetAssignment(string annotationSetId, string imageId, DatasetAssignmentStatus status, bool retest)
        {
            AnnotationSetId = annotationSetId;
            ImageId = imageId;
            Status = status;
            Retest = retest;
        }

        public string AnnotationSetId { get; }
        public string ImageId { get; }
        public DatasetAssignmentStatus Status { get; }
        public bool Retest { get; }
    }

    public sealed class ImageAnnotationSummary
    {
        public ImageAnnotationSummary(string imageId, IReadOnlyList<string> classIds, int annotationCount)
        {
            ImageId = imageId;
            ClassIds = classIds ?? throw new ArgumentNullException(nameof(classIds));
            AnnotationCount = annotationCount;
        }

        public string ImageId { get; private set; }
        public IReadOnlyList<string> ClassIds { get; private set; }
        public int AnnotationCount { get; private set; }
    }

    public sealed class ProjectImage
    {
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

        public string Id { get; }
        public string OriginalFileName { get; }
        public string ManagedFileName { get; }
        public string Sha256 { get; }
        public int Width { get; }
        public int Height { get; }
        public int Sequence { get; }
        public bool IsMissing { get; }
    }

    public interface IProjectLifecycle
    {
        IProjectSession Create(string projectPath);
        IProjectSession Open(string projectPath);
        IProjectSession Restore(string projectPath, string backupPath);
    }
}
