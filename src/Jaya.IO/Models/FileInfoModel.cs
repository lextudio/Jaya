using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Models;

public sealed class FileInfoModel
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public FileSystemItemId? Id { get; init; }
    public long Size { get; init; }
    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Modified { get; init; }
    public DateTimeOffset Accessed { get; init; }
    public FileSystemAttributes Attributes { get; init; }

    public static FileInfoModel FromPath(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("File not found", path);

        return FileSystemModelFactory.FromFileInfo(info);
    }

    public static Task<FileInfoModel> FromPathAsync(string path, CancellationToken ct = default)
        => Task.Run(() => FromPath(path), ct);
}
