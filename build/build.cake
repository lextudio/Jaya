#addin nuget:?package=SharpZipLib
#addin nuget:?package=Cake.Compression

#tool "nuget:?package=gitreleasemanager"

enum OperatingSystem 
{
    Windows,
    MacOS,
    Linux
}

// script arguments and constants
const string APP_NAME = "Jaya File Manager";
var BUILD_NUMBER = EnvironmentVariable("GITHUB_RUN_NUMBER", "1");
var VERSION_PREFIX = Argument("VersionPrefix", "0.0.0");

// public variables
string _versionString = string.Empty;
bool _isGithubActionsBuild;
ConvertableDirectoryPath _sourceDirectory, _buildDirectory, _outputDirectory;
OperatingSystem _operatingSystem;

string GetPath(ConvertableDirectoryPath path)
{
    return MakeAbsolute(path).FullPath;
}

Setup(context => 
{
    _versionString = string.Format("{0}.{1}", VERSION_PREFIX, BUILD_NUMBER);
    _isGithubActionsBuild = GitHubActions.IsRunningOnGitHubActions;

    // Use runtime System.Environment to detect platform (compatible with Cake.Tool v6)
    var platform = System.Environment.OSVersion.Platform;
    switch (platform)
    {
        case System.PlatformID.Unix:
            // macOS and Linux both map to Unix; distinguish by checking for macOS specific file
            // Use presence of /System/Library as heuristic for macOS
            if (System.IO.Directory.Exists("/System/Library"))
                _operatingSystem = OperatingSystem.MacOS;
            else
                _operatingSystem = OperatingSystem.Linux;
            break;

        case System.PlatformID.MacOSX:
            _operatingSystem = OperatingSystem.MacOS;
            break;

        default:
            _operatingSystem = OperatingSystem.Windows;
            break;
    }
    
    _sourceDirectory = Directory("../src");
    _buildDirectory = Directory("../build");
    _outputDirectory = Directory("../publish");
});

Task("BuildMacOSUniversal")
    .IsDependentOn("BuildMacOS64")
    .Does(() =>
{
    if (_operatingSystem != OperatingSystem.MacOS)
    {
        Information("Host is not macOS — skipping BuildMacOSUniversal task.");
        return;
    }

    // Expected per-RID publish locations
    var publishX64 = _outputDirectory + Directory("osx/osx-x64");
    var publishArm64 = _outputDirectory + Directory("osx/osx-arm64");

    if (!DirectoryExists(publishX64) || !DirectoryExists(publishArm64))
    {
        Information("One or both macOS publish directories are missing. Ensure BuildMacOS64 ran first.");
        return;
    }

    // Determine candidate native files to merge: executables (no extension + executable bit) and .dylib files
    var x64Full = MakeAbsolute(publishX64).FullPath;
    var arm64Full = MakeAbsolute(publishArm64).FullPath;
    var universalDir = _outputDirectory + Directory("osx/universal");
    CreateDirectory(universalDir);

    var x64Di = new System.IO.DirectoryInfo(x64Full);
    var arm64Di = new System.IO.DirectoryInfo(arm64Full);
    if (!x64Di.Exists || !arm64Di.Exists)
    {
        Information("Publish directories missing; skipping universal build.");
        return;
    }

    var candidates = new System.Collections.Generic.List<string>();
    // helper: detect Mach-O / fat binaries by checking the first 4 bytes
    Func<string, bool> isMachO = (path) => {
        try {
            var b = System.IO.File.ReadAllBytes(path);
            if (b.Length < 4) return false;
            uint magic = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | ((uint)b[3]);
            // known Mach-O/fat magic numbers
            if (magic == 0xFEEDFACEu || magic == 0xFEEDFACFu || magic == 0xCEFAEDFEu || magic == 0xCFFAEDFEu || magic == 0xCAFEBABEu)
                return true;
            return false;
        }
        catch {
            return false;
        }
    };

    foreach (var fi in x64Di.GetFiles())
    {
        var name = fi.Name;
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();

        // prefer .dylib files when present in both publishes
        if (ext == ".dylib")
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(arm64Full, name)))
                candidates.Add(name);
            continue;
        }

        // consider files with no extension that are Mach-O binaries
        if (string.IsNullOrEmpty(ext))
        {
            var x64Path = System.IO.Path.Combine(x64Full, name);
            var arm64Path = System.IO.Path.Combine(arm64Full, name);
            if (System.IO.File.Exists(arm64Path) && isMachO(x64Path))
            {
                candidates.Add(name);
            }
        }
    }

    if (candidates.Count == 0)
    {
        Information("No native executables or .dylib files found to merge; skipping universal build.");
        return;
    }

    // For each candidate file, run lipo to create a universal variant
    foreach (var name in candidates)
    {
        var x64Path = System.IO.Path.Combine(x64Full, name);
        var arm64Path = System.IO.Path.Combine(arm64Full, name);
        var universalPath = System.IO.Path.Combine(MakeAbsolute(universalDir).FullPath, name);

        // Check architectures of inputs. If they report the same archs, skip lipo and copy arm64 file.
        var tmp1 = System.IO.Path.GetTempFileName();
        var tmp2 = System.IO.Path.GetTempFileName();
        try {
            var infoCmd1 = $"lipo -archs \"{x64Path}\" > \"{tmp1}\"";
            StartProcess("/bin/bash", new ProcessSettings { Arguments = $"-c \"{infoCmd1}\"" });
            var arch1 = System.IO.File.ReadAllText(tmp1).Trim();

            var infoCmd2 = $"lipo -archs \"{arm64Path}\" > \"{tmp2}\"";
            StartProcess("/bin/bash", new ProcessSettings { Arguments = $"-c \"{infoCmd2}\"" });
            var arch2 = System.IO.File.ReadAllText(tmp2).Trim();

            if (!string.IsNullOrEmpty(arch1) && arch1 == arch2)
            {
                Information($"Inputs have same architectures ({arch1}) — skipping lipo and copying arm64 file for {name}.");
                System.IO.File.Copy(arm64Path, universalPath, true);
            }
            else
            {
                var lipoCmd = $"lipo -create \"{x64Path}\" \"{arm64Path}\" -output \"{universalPath}\"";
                Information($"Running: {lipoCmd}");
                var exit = StartProcess("/bin/bash", new ProcessSettings { Arguments = $"-c \"{lipoCmd}\"" });
                if (exit != 0)
                {
                    Information($"lipo failed for {name}; exit code {exit}");
                    throw new Exception($"lipo failed for {name}");
                }
            }
        }
        finally {
            try { System.IO.File.Delete(tmp1); } catch { }
            try { System.IO.File.Delete(tmp2); } catch { }
        }
    }

    // Assemble minimal .app bundle
    var appBundle = _outputDirectory + Directory("Jaya.app");
    var contents = appBundle + Directory("Contents");
    var macos = contents + Directory("MacOS");
    CreateDirectory(macos);

    // Copy Info.plist and resources from existing app bundle if available
    var sourceAppBundle = _buildDirectory + Directory("Jaya.app");
    if (DirectoryExists(sourceAppBundle))
    {
        CopyDirectory(sourceAppBundle + Directory("Contents/Resources"), contents + Directory("Resources"));
        CopyFile(MakeAbsolute(sourceAppBundle + File("Contents/Info.plist")).FullPath, MakeAbsolute(contents + File("Info.plist")).FullPath);
    }

    // Copy merged native files from universal directory into bundle
    var universalFull = MakeAbsolute(universalDir).FullPath;
    var macosFull = MakeAbsolute(macos).FullPath;
    foreach(var f in System.IO.Directory.GetFiles(universalFull))
    {
        var name = System.IO.Path.GetFileName(f);
        var dest = System.IO.Path.Combine(macosFull, name);
        CopyFile(f, dest);

        // if file has no extension, make it executable
        if (string.IsNullOrEmpty(System.IO.Path.GetExtension(name)))
        {
            StartProcess("/bin/chmod", new ProcessSettings { Arguments = $"+x \"{dest}\"" });
        }
    }

    // Zip the .app
    Zip(appBundle, _outputDirectory + File("osx_app_universal.zip"));
    Information("Created osx_app_universal.zip");
});

Teardown(context => 
{

});

Task("BuildInitialization")
    .Does(() => 
{
    Information($"Build is running on {_operatingSystem} operating system{(_isGithubActionsBuild ? " using Github Actions infrastructure" : string.Empty)}.");
    Information($"Source Directory: {GetPath(_sourceDirectory)}");
    Information($"Build Directory: {GetPath(_buildDirectory)}");
    Information($"Version: {_versionString}");

    Information($"Clean up any existing output in directory '{GetPath(_outputDirectory)}'.");
    // Ensure the output directory exists and is cleaned
    CreateDirectory(_outputDirectory);
    CleanDirectory(_outputDirectory);
});

Task("BuildWindows64")
    .IsDependentOn("BuildInitialization")
    .Does(() => 
{
    // Publish for both x64 and arm64 Windows RIDs
    var windowsRIDs = new[] { "win-x64", "win-arm64" };
    foreach(var rid in windowsRIDs)
    {
        var outputDirectory = _outputDirectory + Directory($"windows/{rid}");

        Information($"Build for Windows ({rid}).");
        var settings = new DotNetPublishSettings
        {
            Framework = "net10.0",
            Configuration = "Release",
            SelfContained = true,
            Runtime = rid,
            OutputDirectory = GetPath(outputDirectory)
        };
        DotNetPublish(GetPath(_sourceDirectory), settings);

        Information("Create portable ZIP archive from the build.");
        Zip(outputDirectory, _outputDirectory + File($"windows_portable_{rid}.zip"));

        // Only create Inno Setup installer for win-x64 on Windows hosts
        if (rid == "win-x64" && _operatingSystem == OperatingSystem.Windows)
        {
            Information("Create installation setup.");
            var setupSettings = new InnoSetupSettings
            {
                OutputDirectory = _outputDirectory,
                EnableOutput = true,
                Defines = new Dictionary<string, string> 
                {
                    { "APP_NAME", APP_NAME },
                    { "APP_VERSION", _versionString },
                    { "APP_ROOT", GetPath(Directory("../")) }
                }
            };
            InnoSetup(_buildDirectory + File("setup.iss"), setupSettings);
        }
        else
        {
            if (rid == "win-x64")
            {
                Information("Skipping Inno Setup step (installer) on this machine.");
            }
        }
    }
});

Task("BuildMacOS64")
    .IsDependentOn("BuildInitialization")
    .Does(() => 
{
    if (_operatingSystem != OperatingSystem.MacOS)
    {
        Information("Host is not macOS — skipping BuildMacOS64 task.");
        return;
    }

    // Publish for both x64 and arm64 macOS RIDs
    var macRIDs = new[] { "osx-x64", "osx-arm64" };
    foreach(var rid in macRIDs)
    {
        var outputDirectory = _outputDirectory + Directory($"osx/{rid}");

        Information($"Build for MacOS ({rid}).");
        var settings = new DotNetPublishSettings
        {
            Framework = "net10.0",
            Configuration = "Release",
            SelfContained = true,
            Runtime = rid,
            OutputDirectory = GetPath(outputDirectory)
        };
        DotNetPublish(GetPath(_sourceDirectory), settings);

        Information("Create portable ZIP archive from the build.");
        Zip(outputDirectory, _outputDirectory + File($"osx_portable_{rid}.zip"));

        Information("Create MacOS application bundle (if present).");
        // Only copy the app bundle if it exists in the build directory (some CI or local setups may not have it)
        var appBundleDir = _buildDirectory + Directory("Jaya.app");
        if (DirectoryExists(appBundleDir))
        {
            CopyDirectory(appBundleDir, _outputDirectory + Directory("Jaya.app"));
            CopyDirectory(outputDirectory, _outputDirectory + Directory("Jaya.app/Contents/MacOS"));
            Zip(_outputDirectory + Directory("Jaya.app/Contents/MacOS"), _outputDirectory + File($"osx_app_{rid}.zip"));
        }
        else
        {
            Information("No local Jaya.app bundle found to package; skipping app bundle copy.");
        }
    }
});

Task("BuildLinux64")
    .IsDependentOn("BuildInitialization")
    .Does(() => 
{
    // Publish for both x64 and arm64 Linux RIDs
    var linuxRIDs = new[] { "linux-x64", "linux-arm64" };
    foreach(var rid in linuxRIDs)
    {
        var outputDirectory = _outputDirectory + Directory($"linux/{rid}");

        Information($"Build for Linux ({rid}).");
        var settings = new DotNetPublishSettings
        {
            Framework = "net10.0",
            Configuration = "Release",
            SelfContained = true,
            Runtime = rid,
            OutputDirectory = GetPath(outputDirectory)
        };
        DotNetPublish(GetPath(_sourceDirectory), settings);

        Information("Create portable ZIP archive from the build.");
        Zip(outputDirectory, _outputDirectory + File($"linux_portable_{rid}.zip"));
    }
});


// Deploy task: respects the `OS` argument so we can limit Deploy to a single platform.
var osArg = Argument("OS", "all").ToLowerInvariant();
var deployBuilder = Task("Deploy");
if (osArg == "mac" || osArg == "osx")
{
    deployBuilder = deployBuilder.IsDependentOn("BuildMacOS64").IsDependentOn("BuildMacOSUniversal");
}
else if (osArg == "win" || osArg == "windows")
{
    deployBuilder = deployBuilder.IsDependentOn("BuildWindows64");
}
else if (osArg == "linux")
{
    deployBuilder = deployBuilder.IsDependentOn("BuildLinux64");
}
else
{
    deployBuilder = deployBuilder.IsDependentOn("BuildMacOS64").IsDependentOn("BuildMacOSUniversal").IsDependentOn("BuildWindows64").IsDependentOn("BuildLinux64");
}

deployBuilder.Does(() => { Information("Deploy target completed."); });

RunTarget(Argument("target", "Deploy"));