using Jaya.IO.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Mock;

public class MockFileSystem : Jaya.IO.IFileSystem
{
    readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, List<string>> _dirs = new(StringComparer.Ordinal);

    public MockFileSystem()
    {
        _dirs["/"] = new List<string>();
    }

    public void CreateFile(string path, byte[] content)
    {
        var n = Normalize(path);
        _files[n] = content ?? new byte[0];
        var dir = Path.GetDirectoryName(n) ?? "/";
        _dirs.TryAdd(dir, new List<string>());
    }

    public void CreateDirectory(string path)
    {
        var n = Normalize(path);
        _dirs.TryAdd(n, new List<string>());

        // Ensure parent knows about this directory
        var parent = Path.GetDirectoryName(n) ?? "/";
        _dirs.TryAdd(parent, new List<string>());
        lock (_dirs[parent])
        {
            if (!_dirs[parent].Contains(n))
                _dirs[parent].Add(n);
        }
    }

    public Task<DirectoryInfoModel?> GetDirectoryAsync(string path, bool includeFiles = true, bool includeDirectories = true, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(path);
        if (!_dirs.ContainsKey(normalized))
            return Task.FromResult<DirectoryInfoModel?>(null);

        var files = includeFiles
            ? _files.Keys.Where(k => Path.GetDirectoryName(k) == normalized)
                .Select(k => new FileInfoModel { Name = Path.GetFileName(k), Path = k, Size = _files[k].LongLength, Attributes = FileSystemAttributes.None })
                .ToList()
            : null;

        var directories = includeDirectories
            ? _dirs[normalized].Select(d => new DirectoryInfoModel { Name = Path.GetFileName(d), Path = d, Attributes = FileSystemAttributes.Directory })
                .ToList()
            : null;

        var model = new DirectoryInfoModel { Name = Path.GetFileName(normalized), Path = normalized, Attributes = FileSystemAttributes.Directory, Files = files, Directories = directories };
        return Task.FromResult<DirectoryInfoModel?>(model);
    }

    public Task<FileInfoModel?> GetFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(path);
        if (!_files.ContainsKey(normalized))
            return Task.FromResult<FileInfoModel?>(null);

        return Task.FromResult<FileInfoModel?>(new FileInfoModel { Name = Path.GetFileName(normalized), Path = normalized, Size = _files[normalized].LongLength, Attributes = FileSystemAttributes.None });
    }

    public Task<IReadOnlyList<VolumeModel>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
        var v = new VolumeModel { MountPoint = "/", Name = "Mock" };
        return Task.FromResult<IReadOnlyList<VolumeModel>>(new[] { v });
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(_files.ContainsKey(Normalize(path)) || _dirs.ContainsKey(Normalize(path)));

    public string GetUniqueDestination(string targetDirectory, string name, bool isDirectory)
    {
        var candidate = Path.Combine(targetDirectory, name);
        var i = 1;
        while (_files.ContainsKey(candidate) || _dirs.ContainsKey(candidate))
        {
            candidate = Path.Combine(targetDirectory, isDirectory ? $"{name} ({i})" : $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
            i++;
        }

        return candidate;
    }

    public Task<bool> DeleteAsync(string path, Models.DeleteMode mode = Models.DeleteMode.Permanent, CancellationToken cancellationToken = default)
    {
        var n = Normalize(path);
        var f = _files.TryRemove(n, out _);
        var d = _dirs.TryRemove(n, out _);
        return Task.FromResult(f || d);
    }

    public Task<DeleteResult> DeleteBatchAsync(IEnumerable<string> paths, Models.DeleteMode mode = Models.DeleteMode.Permanent, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        var list = paths is ICollection<string> coll ? coll.Count : paths.Count();
        var results = new List<CopyResult>();
        var jobId = Guid.NewGuid();
        int processed = 0;
        foreach (var p in paths)
        {
            progress?.Report(new TransferProgressReport { JobId = jobId, Stage = TransferStage.ItemStarted, TotalItems = list, ProcessedItems = processed, CurrentSource = p });
            var success = DeleteAsync(p, mode, cancellationToken).GetAwaiter().GetResult();
            processed++;
            results.Add(new CopyResult { SourcePath = p, DestinationPath = p, Success = success });
            progress?.Report(new TransferProgressReport { JobId = jobId, Stage = TransferStage.ItemCompleted, TotalItems = list, ProcessedItems = processed, CurrentSource = p });
        }

        progress?.Report(new TransferProgressReport { JobId = jobId, Stage = TransferStage.Completed, TotalItems = list, ProcessedItems = processed });

        return Task.FromResult(new DeleteResult { Results = results });
    }

    public Task<IReadOnlyList<CopyResult>> TransferAsync(IEnumerable<string> sources, string targetDirectory, TransferMode mode, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        if (mode == TransferMode.Delete)
            throw new ArgumentOutOfRangeException(nameof(mode), "Use DeleteAsync/DeleteBatchAsync for delete operations.");

        var results = new List<CopyResult>();
        foreach (var s in sources)
        {
            var src = Normalize(s);
            var dest = Normalize(Path.Combine(targetDirectory, Path.GetFileName(src)));
            if (_files.ContainsKey(src))
            {
                _files[dest] = _files[src];
                if (mode == TransferMode.Move)
                    _files.TryRemove(src, out _);
                results.Add(new CopyResult { SourcePath = src, DestinationPath = dest, Success = true });
            }
            else if (_dirs.ContainsKey(src))
            {
                _dirs.TryAdd(dest, new List<string>());
                if (mode == TransferMode.Move)
                    _dirs.TryRemove(src, out _);
                results.Add(new CopyResult { SourcePath = src, DestinationPath = dest, Success = true });
            }
            else
            {
                results.Add(new CopyResult { SourcePath = src, DestinationPath = dest, Success = false });
            }
        }

        return Task.FromResult<IReadOnlyList<CopyResult>>(results);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream(_files[Normalize(path)]));

    public Task<Stream> OpenWriteAsync(string path, FileMode mode = FileMode.Create, CancellationToken cancellationToken = default)
    {
        var n = Normalize(path);
        byte[] initial = _files.TryGetValue(n, out var existing) ? existing : new byte[0];
        var ms = new WritableMemoryStream(_files, n, initial, mode);
        return Task.FromResult<Stream>(ms);
    }

    public Task<FileAccessRights> GetAccessRightsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Execute | FileAccessRights.Delete);

    public Task<Jaya.IO.RenameResult> RenameAsync(string sourcePath, string destinationPath, bool overwrite = false, IProgress<TransferProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        var src = Normalize(sourcePath);
        var dst = Normalize(destinationPath);

        try
        {
            // Destination exists handling
            var dstIsFile = _files.ContainsKey(dst);
            var dstIsDir = _dirs.ContainsKey(dst);
            if ((dstIsFile || dstIsDir) && !overwrite)
                return Task.FromResult(new Jaya.IO.RenameResult(false, null, "Destination exists", true));

            if (dstIsFile)
            {
                if (overwrite) _files.TryRemove(dst, out _);
            }
            if (dstIsDir)
            {
                if (overwrite) _dirs.TryRemove(dst, out _);
            }

            if (_files.ContainsKey(src))
            {
                _files[dst] = _files[src];
                _files.TryRemove(src, out _);
                return Task.FromResult(new Jaya.IO.RenameResult(true, dst, null));
            }

            if (_dirs.ContainsKey(src))
            {
                _dirs.TryAdd(dst, _dirs[src]);
                _dirs.TryRemove(src, out _);
                return Task.FromResult(new Jaya.IO.RenameResult(true, dst, null));
            }

            return Task.FromResult(new Jaya.IO.RenameResult(false, null, "Source does not exist"));
        }
        catch (System.Exception ex)
        {
            return Task.FromResult(new Jaya.IO.RenameResult(false, null, ex.Message));
        }
    }

    static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";
        return path.Replace('\\', '/');
    }

    sealed class WritableMemoryStream : MemoryStream
    {
        readonly ConcurrentDictionary<string, byte[]> _files;
        readonly string _path;

        public WritableMemoryStream(ConcurrentDictionary<string, byte[]> files, string path, byte[] initial, FileMode mode)
            : base()
        {
            _files = files;
            _path = path;
            if (initial != null && initial.Length > 0)
            {
                Write(initial, 0, initial.Length);
            }

            if (mode == FileMode.Append)
                Position = initial?.LongLength ?? 0;
            else if (mode == FileMode.Create)
            {
                SetLength(0);
                Position = 0;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _files[_path] = ToArray();
            base.Dispose(disposing);
        }
    }
}
