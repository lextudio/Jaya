using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Jaya.Ui.Services
{
    public static class ClipboardService
    {
        // Synchronous wrapper for existing call sites.
        // Prefer calling CopyTextAsync in new code.
        public static void CopyText(string text)
        {
            CopyTextAsync(text).GetAwaiter().GetResult();
        }

        public static async Task CopyTextAsync(string text, TopLevel? topLevel = null, int timeoutMs = 2000)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Try Avalonia clipboard first (new API: TopLevel.Clipboard -> IClipboard).
            try
            {
                var clipboard = TryGetClipboard(topLevel);
                if (clipboard != null)
                {
                    await WithTimeoutOnUiThreadAsync(
                        () => clipboard.SetTextAsync(text),
                        timeoutMs).ConfigureAwait(false);
                    return;
                }
            }
            catch
            {
                // fall through to process-based fallback
            }

            // Fallback: platform utilities (works even without a TopLevel).
            await CopyTextViaPlatformUtilityAsync(text, timeoutMs).ConfigureAwait(false);
        }

        static IClipboard? TryGetClipboard(TopLevel? topLevel)
        {
            if (topLevel?.Clipboard != null)
                return topLevel.Clipboard;

            var app = Application.Current;
            if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow?.Clipboard;
            }

            if (app?.ApplicationLifetime is ISingleViewApplicationLifetime single)
            {
                // SingleView apps still have a TopLevel; try resolve it from the MainView.
                if (single.MainView is Control c)
                    return TopLevel.GetTopLevel(c)?.Clipboard;
            }

            return null;
        }

        static async Task WithTimeoutOnUiThreadAsync(Func<Task> action, int timeoutMs)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(timeoutMs);

            Task op;

            if (Dispatcher.UIThread.CheckAccess())
            {
                op = action();
            }
            else
            {
                // Clipboard is a TopLevel service; safest to invoke from UI thread.
                // Dispatcher.InvokeAsync(Func<Task>) returns a Task we can wait on later.
                op = Dispatcher.UIThread.InvokeAsync(action);
            }

            var completed = await Task.WhenAny(op, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
            if (completed != op)
                throw new TimeoutException("Clipboard operation timed out.");

            await op.ConfigureAwait(false);
        }

        static async Task CopyTextViaPlatformUtilityAsync(string text, int timeoutMs)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await RunPipeTextAsync("pbcopy", string.Empty, text, timeoutMs).ConfigureAwait(false);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await RunPipeTextAsync("cmd.exe", "/c clip", text, timeoutMs).ConfigureAwait(false);
                    return;
                }

                // Linux: try wl-copy then xclip
                try
                {
                    await RunPipeTextAsync("wl-copy", string.Empty, text, timeoutMs).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    // ignore and try xclip
                }

                await RunPipeTextAsync("xclip", "-selection clipboard", text, timeoutMs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to copy text to clipboard", ex);
            }
        }

        static async Task RunPipeTextAsync(string fileName, string arguments, string text, int timeoutMs)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
                throw new InvalidOperationException($"Failed to start {fileName}");

            await p.StandardInput.WriteAsync(text).ConfigureAwait(false);
            p.StandardInput.Close();

            // Process.WaitForExitAsync exists in modern .NET; fall back to sync wait if not.
            var waitTask = p.WaitForExitAsync();
            var completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (completed != waitTask)
                throw new TimeoutException($"{fileName} timed out");
        }
    }
}
