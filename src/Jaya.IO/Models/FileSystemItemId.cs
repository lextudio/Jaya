using System;
using System.Security.Cryptography;
using System.Text;

namespace Jaya.IO.Models;

public readonly struct FileSystemItemId : IEquatable<FileSystemItemId>
{
    public string Value { get; }

    public FileSystemItemId(string value)
    {
        Value = value ?? string.Empty;
    }

    public bool Equals(FileSystemItemId other)
        => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is FileSystemItemId other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(FileSystemItemId left, FileSystemItemId right)
        => left.Equals(right);

    public static bool operator !=(FileSystemItemId left, FileSystemItemId right)
        => !left.Equals(right);

    public override string ToString() => Value;

    public static bool TryFromPath(string path, out FileSystemItemId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows())
                fullPath = fullPath.ToUpperInvariant();

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
            id = new FileSystemItemId(Convert.ToHexString(bytes));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
