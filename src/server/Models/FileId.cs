using System;
using System.IO;
using System.Runtime.InteropServices;

namespace server.Models;

public static class FileId
{
    public static string Get(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("File not found", path);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsFileId(path);

        return GetUnixFileId(path);
    }

    // ----------------------------
    //          WINDOWS
    // ----------------------------
    private const int FileIdInfo = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] FileId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeHandle hFile,
        int FileInformationClass,
        out FILE_ID_INFO lpFileInformation,
        int dwBufferSize);

    private static string GetWindowsFileId(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (!GetFileInformationByHandleEx(
                fs.SafeFileHandle,
                FileIdInfo,
                out var info,
                Marshal.SizeOf<FILE_ID_INFO>()))
        {
            throw new IOException("Unable to obtain FILE_ID_INFO");
        }

        return $"{info.VolumeSerialNumber:X}-{BitConverter.ToString(info.FileId!).Replace("-", "")}";
    }

    // ----------------------------
    //    LINUX + MACOS (inode)
    // ----------------------------

    // struct stat (Unix)
    [StructLayout(LayoutKind.Sequential)]
    private struct Stat
    {
        public ulong st_dev;
        public ulong st_ino;
        public ulong st_nlink;
        public uint st_mode;
        public uint st_uid;
        public uint st_gid;
        public uint pad0;
        public ulong st_rdev;
        public long st_size;
        public long st_blksize;
        public long st_blocks;

        public Timespec st_atim;
        public Timespec st_mtim;
        public Timespec st_ctim;

        [StructLayout(LayoutKind.Sequential)]
        public struct Timespec
        {
            public long tv_sec;
            public long tv_nsec;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int stat(string path, out Stat buf);

    private static string GetUnixFileId(string path)
    {
        if (stat(path, out var statInfo) != 0)
            throw new IOException("Unable to read inode info (stat)");

        return $"DEV{statInfo.st_dev}-INO{statInfo.st_ino}";
    }
}
