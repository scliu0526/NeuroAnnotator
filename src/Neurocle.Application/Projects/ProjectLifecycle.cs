using System;
using System.Collections.Generic;

namespace Neurocle.Application.Projects
{
    public interface IProjectSession : IDisposable
    {
        string ProjectPath { get; }
        void Save();
        IProjectSession SaveAs(string targetPath);
        IReadOnlyList<ProjectImage> ImportFiles(IEnumerable<string> filePaths);
    }

    public sealed class ProjectImage
    {
        public ProjectImage(string id, string originalFileName, string managedFileName, string sha256, int width, int height, int sequence)
        {
            Id = id;
            OriginalFileName = originalFileName;
            ManagedFileName = managedFileName;
            Sha256 = sha256;
            Width = width;
            Height = height;
            Sequence = sequence;
        }

        public string Id { get; }
        public string OriginalFileName { get; }
        public string ManagedFileName { get; }
        public string Sha256 { get; }
        public int Width { get; }
        public int Height { get; }
        public int Sequence { get; }
    }

    public interface IProjectLifecycle
    {
        IProjectSession Create(string projectPath);
        IProjectSession Open(string projectPath);
    }
}
