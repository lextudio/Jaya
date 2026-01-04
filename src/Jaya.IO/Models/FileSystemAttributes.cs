namespace Jaya.IO.Models;

[Flags]
public enum FileSystemAttributes
{
    None = 0,
    ReadOnly = 1 << 0,
    Hidden = 1 << 1,
    System = 1 << 2,
    Directory = 1 << 3,
    Archive = 1 << 4,
    ReparsePoint = 1 << 5
}
