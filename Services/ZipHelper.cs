using System;
using System.IO;
using System.IO.Compression;

namespace ZenithLauncher.Services;

/// <summary>
/// Safe archive extraction helpers protecting against Zip-Slip path traversal.
/// </summary>
public static class ZipHelper
{
    public static void SafeExtractZip(string zipPath, string destDir, bool overwriteFiles = true)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("Zip path cannot be empty.", nameof(zipPath));
        if (string.IsNullOrWhiteSpace(destDir))
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destDir));

        Directory.CreateDirectory(destDir);

        var destFull = Path.GetFullPath(destDir);
        if (!destFull.EndsWith(Path.DirectorySeparatorChar))
            destFull += Path.DirectorySeparatorChar;
        var destRootNoSep = destFull.TrimEnd(Path.DirectorySeparatorChar);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destFull, entry.FullName));
            if (!targetPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, destRootNoSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Zip entry escapes destination directory: '{entry.FullName}'");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var entryDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(targetPath, overwrite: overwriteFiles);
        }
    }
}
