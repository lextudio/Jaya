using System.Collections.Generic;
using System.IO;

namespace Jaya.IO.Models;

internal static class FileSystemModelFactory
{
    public static FileInfoModel FromFileInfo(FileInfo info)
    {
        FileSystemItemId.TryFromPath(info.FullName, out var id);
        return new FileInfoModel
        {
            Name = info.Name,
            Path = info.FullName,
            Id = id,
            Size = info.Length,
            Created = info.CreationTime,
            Modified = info.LastWriteTime,
            Accessed = info.LastAccessTime,
            Attributes = MapAttributes(info.Attributes)
        };
    }

    public static DirectoryInfoModel FromDirectoryInfo(DirectoryInfo info, IReadOnlyList<FileInfoModel>? files = null, IReadOnlyList<DirectoryInfoModel>? directories = null)
    {
        FileSystemItemId.TryFromPath(info.FullName, out var id);
        return new DirectoryInfoModel
        {
            Name = info.Name,
            Path = info.FullName,
            Id = id,
            Created = info.CreationTime,
            Modified = info.LastWriteTime,
            Accessed = info.LastAccessTime,
            Attributes = MapAttributes(info.Attributes) | FileSystemAttributes.Directory,
            Files = files,
            Directories = directories
        };
    }

    public static FileSystemAttributes MapAttributes(FileAttributes attributes)
    {
        var result = FileSystemAttributes.None;
        if (attributes.HasFlag(FileAttributes.ReadOnly)) result |= FileSystemAttributes.ReadOnly;
        if (attributes.HasFlag(FileAttributes.Hidden)) result |= FileSystemAttributes.Hidden;
        if (attributes.HasFlag(FileAttributes.System)) result |= FileSystemAttributes.System;
        if (attributes.HasFlag(FileAttributes.Directory)) result |= FileSystemAttributes.Directory;
        if (attributes.HasFlag(FileAttributes.Archive)) result |= FileSystemAttributes.Archive;
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) result |= FileSystemAttributes.ReparsePoint;
        return result;
    }
}
