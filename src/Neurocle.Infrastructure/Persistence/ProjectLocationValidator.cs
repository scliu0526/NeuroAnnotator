using System;
using System.IO;

namespace Neurocle.Infrastructure.Persistence
{
    public static class ProjectLocationValidator
    {
        public static void EnsureSupported(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("A project path is required.", nameof(projectPath));
            }

            string fullPath = Path.GetFullPath(projectPath);
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(fullPath));
            if (drive.DriveType != DriveType.Fixed)
            {
                throw new NotSupportedException("Projects must be stored on a local fixed disk.");
            }

            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive) && IsUnder(fullPath, oneDrive))
            {
                throw new NotSupportedException("Projects cannot be stored in a cloud-synchronised directory.");
            }
        }

        private static bool IsUnder(string path, string parent)
        {
            string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }
    }
}
