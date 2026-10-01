using System;

namespace Neurocle.Application.Projects
{
    public interface IProjectSession : IDisposable
    {
        string ProjectPath { get; }
        void Save();
        IProjectSession SaveAs(string targetPath);
    }

    public interface IProjectLifecycle
    {
        IProjectSession Create(string projectPath);
        IProjectSession Open(string projectPath);
    }
}
