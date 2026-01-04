using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Models;

public sealed class DirectoryInfoModel
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public FileSystemItemId? Id { get; init; }
    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Modified { get; init; }
    public DateTimeOffset Accessed { get; init; }
    public FileSystemAttributes Attributes { get; init; }
    public IReadOnlyList<FileInfoModel>? Files { get; init; }
    public IReadOnlyList<DirectoryInfoModel>? Directories { get; init; }

    public static DirectoryInfoModel FromPath(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
            throw new DirectoryNotFoundException(path);

        return FileSystemModelFactory.FromDirectoryInfo(info);
    }

    public static Task<DirectoryInfoModel> FromPathAsync(string path, CancellationToken ct = default)
        => Task.Run(() => FromPath(path), ct);
}
