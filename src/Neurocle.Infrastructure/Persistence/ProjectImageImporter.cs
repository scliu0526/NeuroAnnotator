using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    internal static class ProjectImageImporter
    {
        internal static ProjectImage Read(string sourcePath, string managedFileName, int sequence)
        {
            string extension = Path.GetExtension(sourcePath);
            if (!IsSupported(extension)) throw new NotSupportedException("Unsupported image format.");
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length > 100L * 1024L * 1024L) throw new InvalidDataException("Image size is invalid.");
            using (var image = Image.FromFile(sourcePath, false))
            {
                if (image.Width > 10000 || image.Height > 10000 || (long)image.Width * image.Height > 100000000L) throw new InvalidDataException("Image dimensions are invalid.");
                return new ProjectImage(Guid.NewGuid().ToString("D"), Path.GetFileName(sourcePath), managedFileName, Hash(sourcePath), image.Width, image.Height, sequence);
            }
        }

        private static bool IsSupported(string extension)
        {
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
