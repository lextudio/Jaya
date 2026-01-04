using Jaya.IO.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Platform;

internal class FallbackPlatformFileSystem : IPlatformFileSystem
{
    public Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default)
    {
        var drives = DriveInfo.GetDrives().Select(d => new VolumeModel { MountPoint = d.Name, Name = d.VolumeLabel, IsRemovable = d.DriveType == DriveType.Removable, IsInternal = d.DriveType == DriveType.Fixed }).ToArray();
        return Task.FromResult(drives);
    }

    public Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        // Fallback: no trash implementation.
        return Task.FromResult((false, (string?)null));
    }

    public Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default)
    {
        // No native support in fallback
        return Task.FromResult(false);
    }

    public Task<bool> TryNativeRenameAsync(string source, string dest, bool overwrite, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
