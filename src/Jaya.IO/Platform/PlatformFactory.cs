using System;

namespace Jaya.IO.Platform;

internal static class PlatformFactory
{
    static IPlatformFileSystem? _impl;
    static ManagedTestTrashPlatform? _testTrash;

    public static IPlatformFileSystem Get()
    {
        if (_impl != null)
            return _impl;

        // Select platform-specific implementation where possible
        if (OperatingSystem.IsWindows())
            _impl = new WindowsPlatformFileSystem();
        else if (OperatingSystem.IsMacOS())
            _impl = new MacPlatformFileSystem();
        else if (OperatingSystem.IsLinux())
            _impl = new LinuxPlatformFileSystem();
        else
            _impl = new FallbackPlatformFileSystem();
        return _impl;
    }

    public static void Set(IPlatformFileSystem impl)
    {
        _impl = impl ?? throw new ArgumentNullException(nameof(impl));
    }

    public static string EnableTestTrash()
    {
        if (_testTrash == null)
            _testTrash = new ManagedTestTrashPlatform();
        _impl = _testTrash;
        return _testTrash.TrashRoot;
    }

    // Helper for tests to create default implementation without relying on internal type access
    public static IPlatformFileSystem CreateDefault()
    {
        return Get();
    }
}
