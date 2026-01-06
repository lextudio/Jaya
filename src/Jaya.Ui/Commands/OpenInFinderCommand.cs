using System;
using System.IO;
using System.Diagnostics;
using Serilog;

namespace Jaya.Ui.Commands
{
    public class OpenInFinderCommand : System.Windows.Input.ICommand
    {
        static readonly ILogger Logger = Log.ForContext<OpenInFinderCommand>();

        #pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
        #pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                var target = parameter as string ?? parameter?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(target))
                    return;

                // Normalize common cases: quotes and relative paths
                target = target.Trim().Trim('"');
                try { target = Path.GetFullPath(target); } catch { /* keep original */ }

                var isFile = File.Exists(target);
                var isDirectory = Directory.Exists(target);

                Logger.Information("OpenInFinder requested: {Path}", target);
                if (!isFile && !isDirectory)
                    Logger.Warning("OpenInFinder: path does not exist (will still attempt): {Path}", target);

                if (OperatingSystem.IsMacOS())
                {
                    // macOS: use `open`.
                    // - For files, `open -R <path>` reveals the file in Finder.
                    // - For folders, `open <path>` opens the folder.
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "open",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };

                        if (isFile)
                        {
                            psi.ArgumentList.Add("-R");
                            psi.ArgumentList.Add(target);
                        }
                        else
                        {
                            psi.ArgumentList.Add(target);
                        }

                        Process.Start(psi);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to open Finder for {Path}", target);
                        return;
                    }
                }

                if (OperatingSystem.IsWindows())
                {
                    // Windows: use explorer.
                    // - For files, prefer `explorer /select,"<path>"`.
                    // - For folders, `explorer "<path>"`.
                    try
                    {
                        string args;
                        if (isFile)
                            args = $"/select,\"{target}\"";
                        else
                            args = $"\"{target}\"";

                        Process.Start(new ProcessStartInfo("explorer", args)
                        {
                            UseShellExecute = true,
                        });
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to open Explorer for {Path}", target);
                        return;
                    }
                }

                // Linux / other: try xdg-open
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add(target);
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to open file manager for {Path}", target);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "OpenInFinderCommand failed");
            }
        }
    }
}
