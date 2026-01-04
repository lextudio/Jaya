using System.Collections.Generic;

namespace Jaya.IO.Models;

public sealed class VolumeModel
{
    public string MountPoint { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? DeviceId { get; init; }
    public bool IsRemovable { get; init; }
    public bool IsInternal { get; init; }
    public IReadOnlyList<string>? Flags { get; init; }
}
