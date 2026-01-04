using Jaya.Provider.FileSystem.Models;
using Jaya.Provider.FileSystem.Views;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemServiceMac : INativeFileSystemService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemServiceMac>();

        public async Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            return await Task.Run(() =>
            {
                var model = new DirectoryModel();

                var dirPath = directory?.Path ?? string.Empty;
                if (string.IsNullOrEmpty(dirPath))
                {
                    model.Directories = new List<DirectoryModel>();

                    try
                    {
                        var volumes = MacDriveEnumerator.GetSystemAndExternalVolumes();
                        foreach (var volume in volumes)
                        {
                            var drive = new DirectoryModel(true)
                            {
                                Name = GetMacVolumeName(volume),
                                Path = volume.MountPoint
                            };
                            var isExternal = IsMacVolumeExternal(volume);
                            drive.IsExternalDrive = isExternal;                            
                            Logger.Debug("DirectoryModel {Name}@{Path} IsExternalDrive={IsExternalDrive}", drive.Name, drive.Path, isExternal);
                            model.Directories.Add(drive);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Mac volume enumeration failed");
                    }

                        return model;
                }

                // Fallback to simple directory enumeration for non-root queries
                DirectoryInfo info = new DirectoryInfo(directory?.Path ?? string.Empty);
                model.Name = string.IsNullOrEmpty(info.Name) ? info.FullName : info.Name;
                model.Path = info.FullName;
                model.Created = info.CreationTime;
                model.Modified = info.LastWriteTime;
                model.Accessed = info.LastAccessTime;
                model.IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden);
                model.IsSystem = info.Attributes.HasFlag(FileAttributes.System);

                model.Files = new List<FileModel>();
                try
                {
                    foreach (var fileInfo in info.GetFiles())
                    {
                        var file = new FileModel();
                        // If file has no extension (e.g., dotfiles like ".DS_Store"), keep the full name.
                        // If it has an extension, store name without extension and set the extension value.
                        if (string.IsNullOrEmpty(fileInfo.Extension))
                        {
                            file.Name = fileInfo.Name;
                        }
                        else
                        {
                            // fileInfo.Extension includes the leading dot, e.g. ".txt".
                            // Remove only the trailing extension segment rather than all occurrences.
                            var baseName = fileInfo.Name.Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length);

                            // Defensive: if removing extension leaves empty base (dotfile like ".DS_Store"),
                            // treat the whole name as the Name and leave Extension empty so UI preserves original casing.
                            if (string.IsNullOrEmpty(baseName))
                            {
                                file.Name = fileInfo.Name;
                                file.Extension = string.Empty;
                            }
                            else
                            {
                                file.Name = baseName;
                                // Preserve extension casing — do not force lowercasing
                                file.Extension = fileInfo.Extension.Substring(1);
                            }
                        }
                        file.Path = fileInfo.FullName;
                        file.Size = fileInfo.Length;
                        file.Created = fileInfo.CreationTime;
                        file.Modified = fileInfo.LastWriteTime;
                        file.Accessed = fileInfo.LastAccessTime;
                        file.IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden);
                        file.IsSystem = fileInfo.Attributes.HasFlag(FileAttributes.System);
                        model.Files.Add(file);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Warning(ex, "Unauthorized access listing files in {Path}", directory?.Path);
                    MarkAccessDenied(model, directory?.Path ?? string.Empty);
                }

                model.Directories = new List<DirectoryModel>();
                try
                {
                    foreach (var directoryInfo in info.GetDirectories())
                    {
                        var dir = new DirectoryModel();
                        dir.Name = directoryInfo.Name;
                        dir.Path = directoryInfo.FullName;
                        dir.Created = directoryInfo.CreationTime;
                        dir.Modified = directoryInfo.LastWriteTime;
                        dir.Accessed = directoryInfo.LastAccessTime;
                        dir.IsHidden = directoryInfo.Attributes.HasFlag(FileAttributes.Hidden);
                        dir.IsSystem = directoryInfo.Attributes.HasFlag(FileAttributes.System);
                        model.Directories.Add(dir);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Warning(ex, "Unauthorized access listing directories in {Path}", directory?.Path);
                    MarkAccessDenied(model, directory?.Path ?? string.Empty);
                }

                return model;
            });
        }

        public Task<bool> DeleteAsync(IEnumerable<FileSystemObjectModel> items, DeleteMode mode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            return Task.Run(() =>
            {
                var paths = items
                    .Select(item => item?.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (paths.Count == 0)
                    return false;

                var anyDeleted = false;
                foreach (var path in paths)
                {
                    var deleted = mode == DeleteMode.Trash ? TryMoveToTrash(path!) : TryDelete(path!);
                    if (deleted)
                        anyDeleted = true;
                }

                return anyDeleted;
            });
        }

        // Device helpers and MacDriveEnumerator live in separate file moved later
        static string GetMacVolumeName(MacVolume volume)
        {
            if (!string.IsNullOrEmpty(volume.VolumeName))
                return volume.VolumeName;

            if (volume.MountPoint == "/")
                return "Macintosh HD";

            var trimmed = volume.MountPoint?.TrimEnd(Path.DirectorySeparatorChar);
            var fallback = string.IsNullOrEmpty(trimmed) ? volume.MountPoint : Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(fallback) ? volume.MountPoint : fallback;
        }

        static bool IsMacVolumeExternal(MacVolume volume)
        {
            if (volume.Internal.HasValue)
                return !volume.Internal.Value;

            if (volume.RemovableMedia.HasValue)
                return volume.RemovableMedia.Value;

            if (volume.Ejectable.HasValue)
                return volume.Ejectable.Value;

            if (!string.IsNullOrEmpty(volume.BusProtocol))
            {
                var bp = volume.BusProtocol.Trim();
                if (bp.IndexOf("usb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (bp.IndexOf("thunderbolt", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (bp.IndexOf("firewire", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (bp.IndexOf("fibre", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (!string.IsNullOrEmpty(volume.DeviceIdentifier))
            {
                var dev = volume.DeviceIdentifier.Trim();
                if (dev.StartsWith("disk", StringComparison.OrdinalIgnoreCase))
                {
                    var numPart = dev.Substring(4);
                    if (int.TryParse(new string(numPart.TakeWhile(c => char.IsDigit(c)).ToArray()), out var diskNum))
                    {
                        if (diskNum > 0)
                            return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(volume.MountPoint) && volume.MountPoint != "/")
                return true;

            return false;
        }

        static void MarkAccessDenied(DirectoryModel model, string path)
        {
            if (model == null || string.IsNullOrEmpty(path))
                return;

            if (!string.IsNullOrEmpty(model.AccessErrorMessage))
                return;

            model.AccessErrorMessage = $"Access to {path} is denied.";
        }

        static bool TryMoveToTrash(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // Prefer native APIs so items go to the correct per-volume Trash without requiring Finder automation.
            if (TryMoveToTrashNative(path))
                return true;

            // Last resort: ask Finder to delete (can require Automation permission; may not work headless).
            return TryMoveToTrashWithFinder(path);
        }

        // Native Trash implementation (macOS)
        //
        // Uses CoreServices' FSPathMoveObjectToTrashSync to ask the OS to move an item to Trash.
        // This avoids Apple Events / Finder automation and generally preserves macOS behavior
        // for per-volume Trash locations.
        //
        // Note: Apple documents this API as deprecated in favor of NSFileManager trashItemAtURL:...
        // but it is still widely available on macOS for compatibility. If it's unavailable at runtime,
        // we gracefully fall back to the managed/Finder approaches.
        const string CoreServicesLib = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

        [DllImport(CoreServicesLib, EntryPoint = "FSPathMoveObjectToTrashSync")]
        static extern int FSPathMoveObjectToTrashSync(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourcePath,
            IntPtr targetPath,
            uint options);

        static bool TryMoveToTrashNative(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                    return false;

                // options = 0 for default behavior; targetPath can be NULL if we don't need the resulting path.
                var status = FSPathMoveObjectToTrashSync(path, IntPtr.Zero, 0);
                if (status == 0)
                    return true;

                Logger.Debug("FSPathMoveObjectToTrashSync failed for {Path} (OSStatus={Status})", path, status);
                return false;
            }
            catch (DllNotFoundException ex)
            {
                Logger.Debug(ex, "CoreServices not available; falling back to managed/Finder trash for {Path}", path);
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                Logger.Debug(ex, "FSPathMoveObjectToTrashSync not available; falling back to managed/Finder trash for {Path}", path);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Native trash move failed; falling back to managed/Finder trash for {Path}", path);
                return false;
            }
        }

        static bool TryMoveToTrashWithFinder(string path)
        {
            try
            {
                var script = $"tell application \"Finder\" to delete POSIX file \"{EscapeAppleScriptString(path)}\"";
                var psi = new ProcessStartInfo("osascript")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(script);

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(error))
                        Logger.Warning("Finder trash failed for {Path}: {Error}", path, error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Finder trash failed for {Path}", path);
                return false;
            }
        }

        static string EscapeAppleScriptString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static bool TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

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
