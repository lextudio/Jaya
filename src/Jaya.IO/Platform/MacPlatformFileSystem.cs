using Jaya.IO.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Jaya.IO.Platform;

internal class MacPlatformFileSystem : IPlatformFileSystem
{
    [Flags]
    enum CopyFileFlags : uint
    {
        COPYFILE_DATA = 0x00000001,
        COPYFILE_SECURITY = 0x00000002,
        COPYFILE_XATTR = 0x00000004,
        COPYFILE_STAT = 0x00000008,
        COPYFILE_ACL = 0x00000010,
        COPYFILE_RECURSIVE = 0x00000080,
        COPYFILE_ALL = COPYFILE_DATA | COPYFILE_SECURITY | COPYFILE_XATTR | COPYFILE_STAT | COPYFILE_ACL
    }

    [DllImport("libSystem.dylib", EntryPoint = "copyfile", SetLastError = true)]
    static extern int copyfile([MarshalAs(UnmanagedType.LPUTF8Str)] string from, [MarshalAs(UnmanagedType.LPUTF8Str)] string to, IntPtr state, CopyFileFlags flags);

    public Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default)
    {
        var diskUtilVolumes = TryGetDiskUtilVolumesByMountPoint();
        var drives = DriveInfo.GetDrives();
        var vols = new VolumeModel[drives.Length];
        for (int i = 0; i < drives.Length; i++)
        {
            var drive = drives[i];
            string mountPoint = drive.Name;
            string? volumeName = null;
            string? deviceId = null;
            DiskUtilInfo? info = null;

            if (diskUtilVolumes != null)
            {
                var normalizedMount = NormalizeMountPoint(mountPoint);
                if (!string.IsNullOrEmpty(normalizedMount))
                    diskUtilVolumes.TryGetValue(normalizedMount, out info);
            }

            try
            {
                if (drive.IsReady)
                    volumeName = drive.VolumeLabel;
            }
            catch { }

            var needsMetadata = string.IsNullOrWhiteSpace(volumeName)
                || string.Equals(volumeName, mountPoint, StringComparison.OrdinalIgnoreCase)
                || string.Equals(volumeName, "/", StringComparison.Ordinal);

            if (info == null && needsMetadata)
            {
                info = TryGetDiskUtilInfo(mountPoint);
            }

            if (info != null)
            {
                if (!string.IsNullOrWhiteSpace(info.MountPoint))
                    mountPoint = info.MountPoint!;
                if (!string.IsNullOrWhiteSpace(info.VolumeName))
                    volumeName = info.VolumeName;
                if (!string.IsNullOrWhiteSpace(info.DeviceIdentifier))
                    deviceId = info.DeviceIdentifier;
            }

            vols[i] = new VolumeModel
            {
                MountPoint = mountPoint,
                Name = volumeName,
                DeviceId = deviceId,
                IsRemovable = drive.DriveType == DriveType.Removable,
                IsInternal = drive.DriveType == DriveType.Fixed
            };
        }

        return Task.FromResult(vols);
    }

    const string DiskUtilPath = "/usr/sbin/diskutil";

    sealed class DiskUtilInfo
    {
        public string? VolumeName { get; init; }
        public string? DeviceIdentifier { get; init; }
        public string? MountPoint { get; init; }
    }

    static DiskUtilInfo? TryGetDiskUtilInfo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(DiskUtilPath))
            return null;

        try
        {
            var psi = new ProcessStartInfo(DiskUtilPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("info");
            psi.ArgumentList.Add("-plist");
            psi.ArgumentList.Add(path);

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(new StringReader(output), settings);
            var doc = XDocument.Load(reader);
            var dict = doc.Root?.Element("dict");
            if (dict == null)
                return null;

            var values = ParsePlistDict(dict);

            return new DiskUtilInfo
            {
                VolumeName = GetValue(values, "VolumeName"),
                DeviceIdentifier = GetValue(values, "DeviceIdentifier"),
                MountPoint = GetValue(values, "MountPoint")
            };
        }
        catch
        {
            return null;
        }
    }

    static Dictionary<string, string> ParsePlistDict(XElement dict)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var elements = dict.Elements().ToList();
        for (int i = 0; i < elements.Count - 1; i++)
        {
            if (elements[i].Name.LocalName != "key")
                continue;

            var key = elements[i].Value;
            var valueElement = elements[i + 1];
            if (valueElement.Name.LocalName == "string")
                result[key] = valueElement.Value;

            i++;
        }

        return result;
    }

    static string? GetValue(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : null;
    }

    static Dictionary<string, DiskUtilInfo>? TryGetDiskUtilVolumesByMountPoint()
    {
        if (!File.Exists(DiskUtilPath))
            return null;

        try
        {
            var psi = new ProcessStartInfo(DiskUtilPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("list");
            psi.ArgumentList.Add("-plist");

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(new StringReader(output), settings);
            var doc = XDocument.Load(reader);
            var dicts = doc.Descendants("dict");
            var result = new Dictionary<string, DiskUtilInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var dict in dicts)
            {
                var values = ParsePlistDict(dict);
                var mountPoint = GetValue(values, "MountPoint");
                if (string.IsNullOrWhiteSpace(mountPoint))
                    continue;

                var normalizedMount = NormalizeMountPoint(mountPoint);
                if (string.IsNullOrEmpty(normalizedMount))
                    continue;

                var info = new DiskUtilInfo
                {
                    MountPoint = mountPoint,
                    VolumeName = GetValue(values, "VolumeName"),
                    DeviceIdentifier = GetValue(values, "DeviceIdentifier")
                };

                if (!string.IsNullOrWhiteSpace(info.VolumeName) || !string.IsNullOrWhiteSpace(info.DeviceIdentifier))
                    result[normalizedMount] = info;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    static string NormalizeMountPoint(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return string.Empty;

        var normalized = mountPoint.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(normalized) ? Path.DirectorySeparatorChar.ToString() : normalized;
    }

    public Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult((false, (string?)null));

        cancellationToken.ThrowIfCancellationRequested();

        // Prefer native CoreServices API so items go to per-volume Trash.
            try
            {
                if (TryMoveToTrashNative(path))
                    return Task.FromResult((true, (string?)null));
            }
            catch { }

        // Fallback to asking Finder via osascript
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
                return Task.FromResult((false, (string?)null));

            process.WaitForExit();
            if (process.ExitCode != 0)
                return Task.FromResult((false, (string?)null));

            return Task.FromResult((true, (string?)null));
        }
        catch
        {
            return Task.FromResult((false, (string?)null));
        }
    }

    public Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(false);

            var flags = CopyFileFlags.COPYFILE_ALL;
            if (Directory.Exists(source))
                flags |= CopyFileFlags.COPYFILE_RECURSIVE;

            var r = copyfile(source, dest, IntPtr.Zero, flags);
            return Task.FromResult(r == 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    const string CoreServicesLib = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

    [DllImport(CoreServicesLib, EntryPoint = "FSPathMoveObjectToTrashSync")]
    static extern int FSPathMoveObjectToTrashSync([MarshalAs(UnmanagedType.LPUTF8Str)] string sourcePath, IntPtr targetPath, uint options);

    static bool TryMoveToTrashNative(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            var status = FSPathMoveObjectToTrashSync(path, IntPtr.Zero, 0);
            return status == 0;
        }
        catch
        {
            return false;
        }
    }

    static string EscapeAppleScriptString(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public Task<bool> TryNativeRenameAsync(string source, string dest, bool overwrite, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest))
                return Task.FromResult(false);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(false);

            // If destination exists and overwrite requested, remove it first
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                if (overwrite)
                {
                    try { if (Directory.Exists(dest)) Directory.Delete(dest, true); else File.Delete(dest); } catch { }
                }
                else return Task.FromResult(false);
            }

            // Use libc rename syscall via standard C library
            var result = rename(source, dest);
            return Task.FromResult(result == 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
    static extern int rename([MarshalAs(UnmanagedType.LPUTF8Str)] string oldpath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newpath);
}
