using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    // 將應用層生命週期契約委派給 SQLite 專案儲存實作。
    public sealed class ProjectLifecycle : IProjectLifecycle
    {
        /// <inheritdoc />
        public IProjectSession Create(string projectPath) { return ProjectStore.Create(projectPath); }
        /// <inheritdoc />
        public IProjectSession Open(string projectPath) { return ProjectStore.Open(projectPath); }
        /// <inheritdoc />
        public IProjectSession Restore(string projectPath, string backupPath) { return ProjectStore.Restore(projectPath, backupPath); }
    }
}
