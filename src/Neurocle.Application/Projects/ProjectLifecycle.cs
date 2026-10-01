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
    }
}
