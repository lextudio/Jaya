//
// OpenVsCodeCommand: opens VS Code at specified path
//
using System;
using System.Diagnostics;
using Serilog;

namespace Jaya.Ui.Commands
{
    public class OpenVsCodeCommand : System.Windows.Input.ICommand
    {
        static readonly ILogger Logger = Log.ForContext<OpenVsCodeCommand>();

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                var target = parameter as string ?? parameter?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(target))
                    return;

                Logger.Information("OpenVsCode requested: {Path}", target);

                if (OperatingSystem.IsMacOS())
                {
                    // Use 'code' CLI if available; fallback to 'open -a Visual Studio Code'
                    try
                    {
                        var psi = new ProcessStartInfo("/bin/bash", $"-lc \"code \"\"{target.Replace("\"", "\\\"")}\"\"")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(psi);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to launch code CLI, falling back to open -a");
                        Process.Start(new ProcessStartInfo("open", $"-a \"Visual Studio Code\" \"{target}\"") { UseShellExecute = true });
                        return;
                    }
                }

                if (OperatingSystem.IsWindows())
                {
                    // Use code.exe if available in PATH
                    try
                    {
                        Process.Start(new ProcessStartInfo("cmd", $"/c code \"{target}\"") { CreateNoWindow = true, UseShellExecute = false });
                        return;
                    }
                    catch (Exception)
                    {
                        // fallback: try to open folder with explorer then rely on context menu, but try code anyway
                        Process.Start(new ProcessStartInfo("explorer", $"\"{target}\"") { UseShellExecute = true });
                        return;
                    }
                }

                // Linux / other: try code CLI
                try
                {
                    Process.Start(new ProcessStartInfo("code", $"\"{target}\"") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to launch VS Code for {Path}", target);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "OpenVsCodeCommand failed");
            }
        }
    }
}
