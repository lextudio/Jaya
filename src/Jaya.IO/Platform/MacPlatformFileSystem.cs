using Jaya.IO.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
        var drives = DriveInfo.GetDrives();
        var vols = new VolumeModel[drives.Length];
        for (int i = 0; i < drives.Length; i++)
            vols[i] = new VolumeModel
            {
                MountPoint = drives[i].Name,
                Name = drives[i].VolumeLabel,
                IsRemovable = drives[i].DriveType == DriveType.Removable,
                IsInternal = drives[i].DriveType == DriveType.Fixed
            };

        return Task.FromResult(vols);
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
