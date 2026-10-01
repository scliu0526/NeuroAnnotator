using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    public sealed class ProjectLifecycle : IProjectLifecycle
    {
        public IProjectSession Create(string projectPath) { return ProjectStore.Create(projectPath); }
        public IProjectSession Open(string projectPath) { return ProjectStore.Open(projectPath); }
    }
}
