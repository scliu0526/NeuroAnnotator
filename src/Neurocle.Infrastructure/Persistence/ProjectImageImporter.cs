using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    internal static class ProjectImageImporter
    {
        internal const long MaximumFileSizeBytes = 100L * 1024L * 1024L;
        internal const int MaximumDimension = 10000;
        internal const long MaximumPixelCount = 100000000L;

        internal static ProjectImage Read(string sourcePath, string managedFileName, int sequence)
        {
            string extension = Path.GetExtension(sourcePath);
            if (!IsSupported(extension)) throw new ImageImportException(ImageImportFailure.UnsupportedFormat);
            var info = new FileInfo(sourcePath);
            if (!info.Exists) throw new FileNotFoundException("The image file does not exist.", sourcePath);
            ValidateFileSize(info.Length);
            using (var image = Image.FromFile(sourcePath, false))
            {
                ValidateDimensions(image.Width, image.Height);
                return new ProjectImage(Guid.NewGuid().ToString("D"), Path.GetFileName(sourcePath), managedFileName, Hash(sourcePath), image.Width, image.Height, sequence);
            }
        }

        internal static void ValidateFileSize(long length)
        {
            if (length > MaximumFileSizeBytes) throw new ImageImportException(ImageImportFailure.FileSizeLimit);
        }

        internal static void ValidateDimensions(int width, int height)
        {
            if (width > MaximumDimension || height > MaximumDimension || (long)width * height > MaximumPixelCount) throw new ImageImportException(ImageImportFailure.DimensionLimit);
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
