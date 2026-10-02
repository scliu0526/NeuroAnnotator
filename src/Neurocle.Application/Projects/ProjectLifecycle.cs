using System;
using System.Collections.Generic;

namespace Neurocle.Application.Projects
{
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

    public interface IProjectSession : IDisposable
    {
        string ProjectPath { get; }
        void Save();
        IProjectSession SaveAs(string targetPath);
        IReadOnlyList<ProjectImage> ImportFiles(IEnumerable<string> filePaths);
        ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested);
        ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested);
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
