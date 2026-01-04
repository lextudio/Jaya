using Jaya.IO.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Platform;

internal class LinuxPlatformFileSystem : IPlatformFileSystem
{
    public Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default)
    {
        var drives = DriveInfo.GetDrives();
        var vols = drives.Select(d => new VolumeModel
        {
            MountPoint = d.Name,
            Name = d.VolumeLabel,
            IsRemovable = d.DriveType == DriveType.Removable,
            IsInternal = d.DriveType == DriveType.Fixed
        }).ToArray();
        return Task.FromResult(vols);
    }

    public Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult((false, (string?)null));

        if (!File.Exists(path) && !Directory.Exists(path))
            return Task.FromResult((false, (string?)null));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ok = TryMoveToTrashFreedesktop(path);
            return Task.FromResult((ok, (string?)null));
        }
        catch
        {
            return Task.FromResult((false, (string?)null));
        }
    }

    public Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default)
    {
        // No special native copy; rely on managed copy
        return Task.FromResult(false);
    }

    public Task<bool> TryNativeRenameAsync(string source, string dest, bool overwrite, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest))
                return Task.FromResult(false);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(false);

            if (File.Exists(dest) || Directory.Exists(dest))
            {
                if (overwrite)
                {
                    try { if (Directory.Exists(dest)) Directory.Delete(dest, true); else File.Delete(dest); } catch { }
                }
                else return Task.FromResult(false);
            }

            var res = rename(source, dest);
            return Task.FromResult(res == 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "rename", SetLastError = true)]
    static extern int rename([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string oldpath, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string newpath);

    static bool TryMoveToTrashFreedesktop(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            return false;

        var trashRoot = Path.Combine(home, ".local", "share", "Trash");
        var filesDir = Path.Combine(trashRoot, "files");
        var infoDir = Path.Combine(trashRoot, "info");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        var fullPath = Path.GetFullPath(path);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var uniqueName = GetUniqueTrashName(filesDir, infoDir, name);
        var destination = Path.Combine(filesDir, uniqueName);

        if (Directory.Exists(fullPath))
            Directory.Move(fullPath, destination);
        else
            File.Move(fullPath, destination);

        var infoPath = Path.Combine(infoDir, uniqueName + ".trashinfo");
        var deletionDate = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var escapedPath = EscapeTrashPath(fullPath);
        var contents = $"[Trash Info]{Environment.NewLine}Path={escapedPath}{Environment.NewLine}DeletionDate={deletionDate}{Environment.NewLine}";
        File.WriteAllText(infoPath, contents);

        return true;
    }

    static string GetUniqueTrashName(string filesDir, string infoDir, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = name;
        var counter = 1;
        while (File.Exists(Path.Combine(infoDir, candidate + ".trashinfo"))
               || File.Exists(Path.Combine(filesDir, candidate))
               || Directory.Exists(Path.Combine(filesDir, candidate)))
        {
            candidate = $"{baseName}.{counter}{extension}";
            counter++;
        }

        return candidate;
    }

    static string EscapeTrashPath(string path)
    {
        return Uri.EscapeDataString(path).Replace("%2F", "/");
    }
}
