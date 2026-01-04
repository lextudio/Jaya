using Jaya.IO.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Platform;

internal class ManagedTestTrashPlatform : IPlatformFileSystem
{
    public string TrashRoot { get; }

    public ManagedTestTrashPlatform()
    {
        TrashRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test_trash"));
        Directory.CreateDirectory(TrashRoot);
    }

    public Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default)
    {
        var v = new VolumeModel { MountPoint = "/", Name = "Test" };
        return Task.FromResult(new[] { v });
    }

    public Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult((false, (string?)null));

        try
        {
            var filename = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            var dest = Path.Combine(TrashRoot, filename);
            var i = 1;
            var baseName = Path.GetFileNameWithoutExtension(filename);
            var ext = Path.GetExtension(filename);
            while (File.Exists(dest) || Directory.Exists(dest))
            {
                dest = Path.Combine(TrashRoot, baseName + "." + i + ext);
                i++;
            }

            if (Directory.Exists(path))
                Directory.Move(path, dest);
            else
                File.Move(path, dest);

            return Task.FromResult((true, (string?)dest));
        }
        catch
        {
            return Task.FromResult((false, (string?)null));
        }
    }

    public Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> TryNativeRenameAsync(string source, string dest, bool overwrite, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest))
                return Task.FromResult(false);

            if (File.Exists(source))
            {
                if (File.Exists(dest))
                {
                    if (overwrite) File.Delete(dest); else return Task.FromResult(false);
                }
                File.Move(source, dest);
                return Task.FromResult(true);
            }

            if (Directory.Exists(source))
            {
                if (Directory.Exists(dest))
                {
                    if (overwrite) Directory.Delete(dest, true); else return Task.FromResult(false);
                }
                Directory.Move(source, dest);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
