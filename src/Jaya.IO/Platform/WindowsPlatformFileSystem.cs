using Jaya.IO.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.IO.Platform;

internal class WindowsPlatformFileSystem : IPlatformFileSystem
{
    public Task<VolumeModel[]> EnumerateVolumesAsync(CancellationToken cancellationToken = default)
    {
        var drives = DriveInfo.GetDrives();
        var vols = new VolumeModel[drives.Length];
        for (int i = 0; i < drives.Length; i++)
        {
            vols[i] = new VolumeModel { MountPoint = drives[i].Name, Name = drives[i].VolumeLabel, IsRemovable = drives[i].DriveType == DriveType.Removable, IsInternal = drives[i].DriveType == DriveType.Fixed };
        }
        return Task.FromResult(vols);
    }

    public Task<(bool Success, string? TrashPath)> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        // Try native Windows shell trash (Recycle Bin) via SHFileOperation.
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult((false, (string?)null));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var from = path + '\0' + '\0';
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };

            var result = SHFileOperation(ref operation);
                if (result == 0 && !operation.fAnyOperationsAborted)
                    return Task.FromResult((true, (string?)null));
        }
        catch
        {
            // ignore and try managed delete fallback below
        }

        // fallback to managed delete
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
            else return Task.FromResult((false, (string?)null));

            return Task.FromResult((true, (string?)null));
        }
        catch
        {
            return Task.FromResult((false, (string?)null));
        }
    }

    public Task<bool> TryNativeCopyAsync(string source, string dest, IProgress<TransferProgressReport>? progress, CancellationToken cancellationToken = default)
    {
        // Implement a simple native file copy using CopyFileEx where possible. Directories are not handled here.
        try
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest))
                return Task.FromResult(false);

            if (Directory.Exists(source))
                return Task.FromResult(false);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(false);

            var cancel = false;
            CopyProgressRoutine? callback = null;
            var ok = CopyFileEx(source, dest, callback, IntPtr.Zero, ref cancel, CopyFileFlags.COPY_FILE_RESTARTABLE);
            return Task.FromResult(ok);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    const uint FO_DELETE = 0x0003;
    const ushort FOF_ALLOWUNDO = 0x0040;
    const ushort FOF_NOCONFIRMATION = 0x0010;
    const ushort FOF_NOERRORUI = 0x0400;
    const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    delegate CopyProgressResult CopyProgressRoutine(
        long totalFileSize,
        long totalBytesTransferred,
        long streamSize,
        long streamBytesTransferred,
        uint dwStreamNumber,
        CopyProgressCallbackReason dwCallbackReason,
        IntPtr hSourceFile,
        IntPtr hDestinationFile,
        IntPtr lpData);

    [Flags]
    enum CopyFileFlags : uint
    {
        COPY_FILE_RESTARTABLE = 0x00000002
    }

    enum CopyProgressResult : uint
    {
        PROGRESS_CONTINUE = 0,
        PROGRESS_CANCEL = 1,
        PROGRESS_STOP = 2,
        PROGRESS_QUIET = 3
    }

    enum CopyProgressCallbackReason : uint
    {
        CALLBACK_CHUNK_FINISHED = 0,
        CALLBACK_STREAM_SWITCH = 1
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CopyFileEx(
        string lpExistingFileName,
        string lpNewFileName,
        CopyProgressRoutine lpProgressRoutine,
        IntPtr lpData,
        ref bool pbCancel,
        CopyFileFlags dwCopyFlags);
}
