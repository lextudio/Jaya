namespace Jaya.IO.Models;

[Flags]
public enum FileAccessRights
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Execute = 1 << 2,
    Delete = 1 << 3
}
