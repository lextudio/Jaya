using Jaya.IO.Models;
using Jaya.IO.Platform;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO;

internal class PlatformFileSystem : IFileSystem
{
    readonly IPlatformFileSystem _platform;
    readonly Transfer.TransferEngine _transfer;

    public PlatformFileSystem()
    {
        _platform = Platform.PlatformFactory.Get();
        _transfer = new Transfer.TransferEngine(_platform);
    }

    public Task<DirectoryInfoModel?> GetDirectoryAsync(string path, bool includeFiles = true, bool includeDirectories = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<DirectoryInfoModel?>(null);

        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo info;
        try
        {
            info = new DirectoryInfo(path);
            if (!info.Exists)
                return Task.FromResult<DirectoryInfoModel?>(null);
        }
        catch
        {
            return Task.FromResult<DirectoryInfoModel?>(null);
        }

        var files = includeFiles ? new List<FileInfoModel>() : null;
        var directories = includeDirectories ? new List<DirectoryInfoModel>() : null;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0
        };

        if (includeFiles)
        {
            try
            {
                foreach (var f in info.EnumerateFiles("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        files!.Add(FileSystemModelFactory.FromFileInfo(f));
                    }
                    catch
                    {
                        // Skip entries we cannot inspect.
                    }
                }
            }
            catch
            {
                files = new List<FileInfoModel>();
            }
        }

        if (includeDirectories)
        {
            try
            {
                foreach (var d in info.EnumerateDirectories("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        directories!.Add(FileSystemModelFactory.FromDirectoryInfo(d));
                    }
                    catch
                    {
                        // Skip entries we cannot inspect.
                    }
                }
            }
            catch
            {
                directories = new List<DirectoryInfoModel>();
            }
        }

        var model = FileSystemModelFactory.FromDirectoryInfo(info, files, directories);
        return Task.FromResult<DirectoryInfoModel?>(model);
    }

    public Task<FileInfoModel?> GetFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<FileInfoModel?>(null);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
                return Task.FromResult<FileInfoModel?>(null);

            return Task.FromResult<FileInfoModel?>(FileSystemModelFactory.FromFileInfo(fi));
        }
        catch
        {
            return Task.FromResult<FileInfoModel?>(null);
        }
    }

    public async Task<IReadOnlyList<VolumeModel>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
        var volumes = await _platform.EnumerateVolumesAsync(cancellationToken).ConfigureAwait(false);
        return volumes;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(path) || Directory.Exists(path));

    public string GetUniqueDestination(string targetDirectory, string name, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new ArgumentNullException(nameof(targetDirectory));

        var baseName = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = name;

        var candidate = Path.Combine(targetDirectory, baseName + extension);
        var counter = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            var suffix = counter == 1 ? " copy" : $" copy {counter}";
            candidate = Path.Combine(targetDirectory, baseName + suffix + extension);
            counter++;
        }

        return candidate;
    }

    public Task<bool> DeleteAsync(string path, Models.DeleteMode mode = Models.DeleteMode.Permanent, CancellationToken cancellationToken = default)
        => _transfer.DeleteAsync(path, mode, cancellationToken);

    public Task<DeleteResult> DeleteBatchAsync(IEnumerable<string> paths, Models.DeleteMode mode = Models.DeleteMode.Permanent, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => _transfer.DeleteBatchAsync(paths, mode, progress, cancellationToken);

    public Task<IReadOnlyList<CopyResult>> TransferAsync(IEnumerable<string> sources, string targetDirectory, TransferMode mode, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => _transfer.TransferAsync(sources, targetDirectory, mode, progress, cancellationToken);

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream>(stream);
    }

    public Task<Stream> OpenWriteAsync(string path, FileMode mode = FileMode.Create, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(path, mode, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        return Task.FromResult<Stream>(stream);
    }

    public Task<FileAccessRights> GetAccessRightsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(FileAccessRights.None);

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path) && !Directory.Exists(path))
            return Task.FromResult(FileAccessRights.None);

        try
        {
            return Task.FromResult(OperatingSystem.IsWindows()
                ? GetWindowsAccessRights(path)
                : GetUnixAccessRights(path));
        }
        catch
        {
            return Task.FromResult(FileAccessRights.None);
        }
    }

    static FileAccessRights GetWindowsAccessRights(string path)
    {
        var rights = FileAccessRights.Read;
        var attributes = File.GetAttributes(path);
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);

        if (!attributes.HasFlag(FileAttributes.ReadOnly))
            rights |= FileAccessRights.Write;

        if (isDirectory || IsLikelyExecutable(path))
            rights |= FileAccessRights.Execute;

        if (rights.HasFlag(FileAccessRights.Write))
            rights |= FileAccessRights.Delete;

        return rights;
    }

    static FileAccessRights GetUnixAccessRights(string path)
    {
        FileAccessRights rights = FileAccessRights.None;
        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
                rights |= FileAccessRights.Read;
            if ((mode & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                rights |= FileAccessRights.Write;
            if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                rights |= FileAccessRights.Execute;
            if (rights.HasFlag(FileAccessRights.Write))
                rights |= FileAccessRights.Delete;
        }
        catch
        {
            return FileAccessRights.None;
        }

        return rights;
    }

    static bool IsLikelyExecutable(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return false;

        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }
}
