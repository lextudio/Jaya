using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jaya.Ui.Services
{
    public static class ClipboardService
    {
        public static void CopyText(string text)
        {
            if (text == null)
                return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var psi = new ProcessStartInfo("pbcopy")
                    {
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null)
                        throw new InvalidOperationException("Failed to start pbcopy");
                    p.StandardInput.Write(text);
                    p.StandardInput.Close();
                    p.WaitForExit(2000);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c clip")
                    {
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null)
                        throw new InvalidOperationException("Failed to start clip");
                    p.StandardInput.Write(text);
                    p.StandardInput.Close();
                    p.WaitForExit(2000);
                    return;
                }

                // Linux: try wl-copy then xclip
                try
                {
                    var psi = new ProcessStartInfo("wl-copy")
                    {
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        p.StandardInput.Write(text);
                        p.StandardInput.Close();
                        p.WaitForExit(2000);
                        return;
                    }
                }
                catch { }

                try
                {
                    var psi = new ProcessStartInfo("xclip", "-selection clipboard")
                    {
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null)
                        throw new InvalidOperationException("Failed to start xclip");
                    p.StandardInput.Write(text);
                    p.StandardInput.Close();
                    p.WaitForExit(2000);
                    return;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("No clipboard utility available (wl-copy/xclip)", ex);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to copy text to clipboard", ex);
            }
        }
    }
}
