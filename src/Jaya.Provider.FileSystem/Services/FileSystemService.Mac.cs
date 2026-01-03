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

namespace Jaya.Provider.FileSystem.Services
{
    public class FileSystemServiceMac : INativeFileSystemService
    {
        static readonly ILogger Logger = Log.ForContext<FileSystemServiceMac>();

        public async Task<DirectoryModel> GetDirectoryAsync(AccountModelBase account, DirectoryModel directory = null)
        {
            return await Task.Run(() =>
            {
                var model = new DirectoryModel();

                if (string.IsNullOrEmpty(directory.Path))
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
                DirectoryInfo info = new DirectoryInfo(directory.Path);
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
                        if (string.IsNullOrEmpty(fileInfo.Extension))
                            file.Name = fileInfo.Name;
                        else
                        {
                            file.Name = fileInfo.Name.Replace(fileInfo.Extension, string.Empty);
                            file.Extension = fileInfo.Extension.Substring(1).ToLowerInvariant();
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
                    Log.Warning(ex, "Unauthorized access listing files in {Path}", directory.Path);
                    MarkAccessDenied(model, directory.Path);
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
                    Log.Warning(ex, "Unauthorized access listing directories in {Path}", directory.Path);
                    MarkAccessDenied(model, directory.Path);
                }

                return model;
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
    }
}
