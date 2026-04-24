using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskScout.Helpers.Win32;

internal static partial class Win32Native
{
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_NO_MORE_FILES = 18;
    public const int ERROR_INVALID_HANDLE = 6;

    public const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    public const int FILE_ATTRIBUTE_HIDDEN = 0x2;
    public const int FILE_ATTRIBUTE_SYSTEM = 0x4;
    public const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
    public const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
    public const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;

    public const int FIND_FIRST_EX_LARGE_FETCH = 0x2;

    public const int FindExInfoBasic = 1;
    public const int FindExSearchNameMatch = 0;

    public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    public const uint IO_REPARSE_TAG_CLOUD = 0x9000001A;
    public const uint IO_REPARSE_TAG_CLOUD_1 = 0x9000101A;
    public const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
    public const uint IO_REPARSE_TAG_ONEDRIVE = 0x9000702A;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0; // reparse tag when FILE_ATTRIBUTE_REPARSE_POINT set
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;

        public readonly long FileSizeBytes =>
            ((long)nFileSizeHigh << 32) | nFileSizeLow;

        public readonly bool IsDirectory =>
            (dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

        public readonly bool IsReparsePoint =>
            (dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

        public readonly bool IsCloudPlaceholder =>
            (dwFileAttributes & (FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS | FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_OFFLINE)) != 0;

        public readonly DateTime LastWriteUtc =>
            FileTimeToDateTimeUtc(ftLastWriteTime);

        private static DateTime FileTimeToDateTimeUtc(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            try
            {
                long ticks = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
                if (ticks <= 0) return DateTime.MinValue;
                return DateTime.FromFileTimeUtc(ticks);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstFileExW")]
    public static extern SafeFindHandle FindFirstFileEx(
        string lpFileName,
        int fInfoLevelId,
        out WIN32_FIND_DATAW lpFindFileData,
        int fSearchOp,
        IntPtr lpSearchFilter,
        int dwAdditionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextFileW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindNextFile(SafeFindHandle hFindFile, out WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindClose(IntPtr hFindFile);
}

public sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeFindHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => Win32Native.FindClose(handle);
}
