using Jaya.IO.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Platform;

public interface IPlatformFileSystem
{
    Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default);
    // Returns (success, trashPath-if-known)
    Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default);
}
