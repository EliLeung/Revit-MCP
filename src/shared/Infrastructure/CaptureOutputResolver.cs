using System;
using System.IO;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Resolves Revit ImageExport output paths. Revit appends view metadata to the requested basename.
    /// </summary>
    public static class CaptureOutputResolver
    {
        public static string FindActualOutput(string requestedPath, string format)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                return null;

            try
            {
                var directory = Path.GetDirectoryName(requestedPath);
                if (string.IsNullOrWhiteSpace(directory))
                    return null;

                directory = Path.GetFullPath(directory);
                if (!Directory.Exists(directory))
                    return null;

                var baseName = Path.GetFileNameWithoutExtension(requestedPath);
                if (string.IsNullOrEmpty(baseName))
                    return null;

                var extension = format == "jpeg" ? "jpg" : format;
                var exact = Path.Combine(directory, baseName + "." + extension);
                if (File.Exists(exact))
                    return exact;

                string best = null;
                var bestTime = DateTime.MinValue;

                foreach (var candidate in Directory.EnumerateFiles(directory, baseName + "*." + extension))
                {
                    var fileName = Path.GetFileName(candidate);
                    if (!IsRevitExportMatch(fileName, baseName, extension))
                        continue;

                    try
                    {
                        var writeTime = File.GetLastWriteTimeUtc(candidate);
                        if (writeTime >= bestTime)
                        {
                            bestTime = writeTime;
                            best = candidate;
                        }
                    }
                    catch
                    {
                        // Ignore unreadable legacy files in captures/.
                    }
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsRevitExportMatch(string fileName, string baseName, string extension)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(baseName))
                return false;

            if (fileName.Equals(baseName + "." + extension, StringComparison.OrdinalIgnoreCase))
                return true;

            var prefix = baseName + " ";
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
