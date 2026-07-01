using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>Mirrors the native <c>FDICABINETINFO</c> struct from fdi.h, populated by <c>FDIIsCabinet</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FDICABINETINFO
{
    public int cbCabinet;
    public ushort cFolders;
    public ushort cFiles;
    public ushort setID;
    public ushort iCabinet;

    [MarshalAs(UnmanagedType.Bool)]
    public bool fReserve;

    public short iFolder;
    public int fdie;
}

/// <summary>
/// Mirrors the native <c>FDINOTIFICATION</c> struct from fdi.h — passed by pointer to <c>FNFDINOTIFY</c>
/// for every notification <c>FDICopy</c> raises while extracting or enumerating a cabinet.
/// <c>psz1</c>/<c>psz2</c>/<c>psz3</c> are ANSI C strings owned by cabinet.dll; decode them with
/// <see cref="Marshal.PtrToStringAnsi(IntPtr)"/> rather than treating them as managed strings, since
/// <see cref="Marshal.StructureToPtr{T}(T, IntPtr, bool)"/>/<c>PtrToStructure</c> marshal this struct
/// blittably (no automatic ANSI decoding on the char* fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FDINOTIFICATION
{
    public int cb;
    public IntPtr psz1;
    public IntPtr psz2;
    public IntPtr psz3;
    public IntPtr pv;

    public ushort date;
    public ushort time;
    public ushort attribs;

    public ushort setID;
    public ushort iCabinet;
    public ushort iFolder;
    public int fdie;

    /// <summary>Valid only for <see cref="FdiNotificationType.CloseFileInfo"/>: the token returned from the matching <see cref="FdiNotificationType.CopyFile"/>.</summary>
    public IntPtr hf;
}

/// <summary>Notification codes passed to <c>FNFDINOTIFY</c> (<c>FDINOTIFICATIONTYPE</c> enum in fdi.h).</summary>
internal enum FdiNotificationType
{
    CabinetInfo = 0,
    PartialFile = 1,
    CopyFile = 2,
    CloseFileInfo = 3,
    NextCabinet = 4,
    Enumerate = 5,
}

/// <summary>FDI error codes (<c>FDIERROR</c> enum in fdi.h), reported via <see cref="ERF.erfOper"/>.</summary>
internal enum FdiErrorCode
{
    None = 0,
    CabinetNotFound = 1,
    NotACabinet = 2,
    UnknownCabinetVersion = 3,
    CorruptCabinet = 4,
    AllocFail = 5,
    BadCompressionType = 6,
    MdiFail = 7,
    TargetFile = 8,
    ReserveMismatch = 9,
    WrongCabinet = 10,
    UserAbort = 11,
}

internal static class FdiConstants
{
    /// <summary>cpuUNKNOWN from fdi.h — let cabinet.dll pick the decompressor for the running CPU.</summary>
    public const int CpuUnknown = -1;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr FdiAllocDelegate(uint cb);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void FdiFreeDelegate(IntPtr memory);

// FDI's open/read/write/close/seek delegates are simpler than FCI's: no `out err` or `pv` context
// parameter is threaded through per call (see fdi.h PFNOPEN/PFNREAD/PFNWRITE/PFNCLOSE/PFNSEEK) — the
// only failure signal is the return value itself (-1/((UINT)-1) on error).
[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal delegate IntPtr FdiOpenDelegate(string pszFile, int oflag, int pmode);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint FdiReadDelegate(IntPtr hf, IntPtr memory, uint cb);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint FdiWriteDelegate(IntPtr hf, IntPtr memory, uint cb);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FdiCloseDelegate(IntPtr hf);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FdiSeekDelegate(IntPtr hf, int dist, int seektype);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr FdiNotifyDelegate(FdiNotificationType fdint, ref FDINOTIFICATION pfdin);
