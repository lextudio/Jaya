using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Jaya.Provider.FileSystem.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemServiceLinux : INativeFileSystemService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemServiceLinux>();

        public Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            // Simple Linux implementation using /proc/mounts or lsblk could be added.
            return Task.FromResult<DirectoryModel?>(new DirectoryModel());
        }

        public Task<bool> DeleteAsync(IEnumerable<FileSystemObjectModel> items, DeleteMode mode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            return Task.Run(() =>
            {
                var anyDeleted = false;
                foreach (var item in items)
                {
                    var path = item?.Path;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var deleted = mode == DeleteMode.Trash ? TryMoveToTrash(path) : TryDelete(path);
                    if (deleted)
                        anyDeleted = true;
                }

                return anyDeleted;
            });
        }

        static bool TryMoveToTrash(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (TryMoveToUserTrash(path))
                return true;

            return TryMoveToTrashWithGio(path);
        }

        static bool TryMoveToUserTrash(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    return false;

                var trashBase = GetTrashBaseDirectory();
                var filesDir = Path.Combine(trashBase, "files");
                var infoDir = Path.Combine(trashBase, "info");
                Directory.CreateDirectory(filesDir);
                Directory.CreateDirectory(infoDir);

                var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name))
                    return false;

                var uniqueName = GetUniqueTrashName(filesDir, infoDir, name);
                var targetPath = Path.Combine(filesDir, uniqueName);
                var infoPath = Path.Combine(infoDir, $"{uniqueName}.trashinfo");

                if (Directory.Exists(fullPath))
                    Directory.Move(fullPath, targetPath);
                else
                    File.Move(fullPath, targetPath);

                var infoContents = BuildTrashInfo(fullPath);
                File.WriteAllText(infoPath, infoContents);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to move to trash at {Path}", path);
                return false;
            }
        }

        static bool TryMoveToTrashWithGio(string path)
        {
            try
            {
                var psi = new ProcessStartInfo("gio")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("trash");
                psi.ArgumentList.Add(path);

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                process.WaitForExit();
                if (process.ExitCode == 0)
                    return true;

                var error = process.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(error))
                    Logger.Debug("gio trash failed for {Path}: {Error}", path, error);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "gio trash failed for {Path}", path);
            }

            return false;
        }

        static string GetTrashBaseDirectory()
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
                return Path.Combine(xdgDataHome, "Trash");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "Trash");
        }

        static string GetUniqueTrashName(string filesDir, string infoDir, string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(baseName))
                baseName = fileName;

            var extension = Path.GetExtension(fileName);
            var candidate = fileName;
            var counter = 2;

            while (File.Exists(Path.Combine(filesDir, candidate)) ||
                   Directory.Exists(Path.Combine(filesDir, candidate)) ||
                   File.Exists(Path.Combine(infoDir, $"{candidate}.trashinfo")))
            {
                candidate = $"{baseName} {counter}{extension}";
                counter++;
            }

            return candidate;
        }

        static string BuildTrashInfo(string originalPath)
        {
            var escapedPath = EscapeTrashInfoPath(originalPath);
            var deletionDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            return $"[Trash Info]\nPath={escapedPath}\nDeletionDate={deletionDate}\n";
        }

        static string EscapeTrashInfoPath(string path)
        {
            var normalized = Path.GetFullPath(path).Replace('\\', '/');
            if (!normalized.StartsWith("/", StringComparison.Ordinal))
                return Uri.EscapeDataString(normalized);

            var segments = normalized.Split('/', StringSplitOptions.None);
            var escaped = new List<string>(segments.Length);
            for (var index = 0; index < segments.Length; index++)
            {
                if (index == 0)
                    continue;

                escaped.Add(Uri.EscapeDataString(segments[index]));
            }

            return "/" + string.Join("/", escaped);
        }

        static bool TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    return true;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to delete {Path}", path);
            }

            return false;
        }
    }
}
