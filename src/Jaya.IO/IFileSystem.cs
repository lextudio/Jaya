using Jaya.IO.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO
{
    public interface IFileSystem
    {
        Task<DirectoryInfoModel?> GetDirectoryAsync(string path, bool includeFiles = true, bool includeDirectories = true, CancellationToken cancellationToken = default);
        Task<FileInfoModel?> GetFileAsync(string path, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<VolumeModel>> GetVolumesAsync(CancellationToken cancellationToken = default);

        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
        string GetUniqueDestination(string targetDirectory, string name, bool isDirectory);

        Task<bool> DeleteAsync(string path, Models.DeleteMode mode = Models.DeleteMode.Permanent, CancellationToken cancellationToken = default);
        Task<DeleteResult> DeleteBatchAsync(IEnumerable<string> paths, Models.DeleteMode mode = Models.DeleteMode.Permanent, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CopyResult>> TransferAsync(IEnumerable<string> sources, string targetDirectory, TransferMode mode, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default);

        Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);
        Task<Stream> OpenWriteAsync(string path, FileMode mode = FileMode.Create, CancellationToken cancellationToken = default);

        Task<FileAccessRights> GetAccessRightsAsync(string path, CancellationToken cancellationToken = default);
        Task<RenameResult> RenameAsync(string sourcePath, string destinationPath, bool overwrite = false, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default);
    }

    public record RenameResult(bool Success, string? DestinationPath, string? Error, bool Conflict = false);

    public static class FileSystem
    {
        public static IFileSystem Default { get; set; } = new PlatformFileSystem();

        // Test helper: enable a managed test trash implementation and return its root path.
        public static string EnableManagedTestTrash()
        {
            var root = Platform.PlatformFactory.EnableTestTrash();
            // recreate default to pick up the new platform implementation
            Default = new PlatformFileSystem();
            return root;
        }

        // Recover a trashed item by moving it from the provided trash path back to the intended destination.
        public static async Task<bool> RecoverFromTrashAsync(string trashPath, string destination)
        {
            if (string.IsNullOrWhiteSpace(trashPath) || string.IsNullOrWhiteSpace(destination))
                return false;

            try
            {
                // If the exact path doesn't exist, try to find a likely candidate in the same directory
                if (!File.Exists(trashPath) && !Directory.Exists(trashPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(trashPath) ?? string.Empty;
                        var baseName = Path.GetFileNameWithoutExtension(trashPath);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            var candidate = Directory.GetFiles(dir).FirstOrDefault(f => Path.GetFileName(f).StartsWith(baseName, StringComparison.Ordinal));
                            if (!string.IsNullOrWhiteSpace(candidate))
                                trashPath = candidate;
                        }
                    }
                    catch
                    {
                        // ignore and proceed to existence checks below
                    }
                }

                // Try a straight move first
                if (File.Exists(trashPath) || Directory.Exists(trashPath))
                {
                    var destDir = Path.GetDirectoryName(destination) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    // Use a robust copy-then-delete approach to avoid move semantics causing issues across filesystems.
                    if (Directory.Exists(trashPath))
                    {
                        CopyDirectory(trashPath, destination);
                        try { Directory.Delete(trashPath, true); } catch { }
                        return true;
                    }
                    else
                    {
                        try
                        {
                            // Stream copy to ensure exact content
                            using var srcStream = File.OpenRead(trashPath);
                            var destDir2 = Path.GetDirectoryName(destination) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(destDir2) && !Directory.Exists(destDir2))
                                Directory.CreateDirectory(destDir2);

                            using var dstStream = File.Create(destination);
                            await srcStream.CopyToAsync(dstStream).ConfigureAwait(false);
                        }
                        catch
                        {
                            return false;
                        }

                        try { File.Delete(trashPath); } catch { }
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }

            static void CopyDirectory(string src, string dst)
            {
                var dir = new DirectoryInfo(src);
                if (!dir.Exists) return;
                Directory.CreateDirectory(dst);
                foreach (var file in dir.EnumerateFiles())
                {
                    var targetFilePath = Path.Combine(dst, file.Name);
                    File.Copy(file.FullName, targetFilePath, true);
                }

                foreach (var subdir in dir.EnumerateDirectories())
                {
                    var targetSubDir = Path.Combine(dst, subdir.Name);
                    CopyDirectory(subdir.FullName, targetSubDir);
                }
            }
        }
    }
}
