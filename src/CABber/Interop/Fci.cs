using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>Mirrors the native <c>CCAB</c> struct from fci.h/fdi.h (cabinet creation parameters).</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct CCAB
{
    public int cb;
    public int cbFolderThresh;
    public uint cbReserveCFHeader;
    public uint cbReserveCFFolder;
    public uint cbReserveCFData;
    public int iCab;
    public int iDisk;
    public int fFailOnIncompressible;
    public ushort setID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FciConstants.MaxDiskName)]
    public string szDisk;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FciConstants.MaxCabinetName)]
    public string szCab;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FciConstants.MaxCabPath)]
    public string szCabPath;
}

/// <summary>Mirrors the native <c>ERF</c> struct — the last-error slot shared by FCI and FDI.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ERF
{
    public int erfOper;
    public int erfType;
    public int fError;
}

/// <summary>FCI error codes (<c>FCIERROR</c> enum in fci.h), reported via <see cref="ERF.erfOper"/>.</summary>
internal enum FciErrorCode
{
    None = 0,
    OpenSource = 1,
    ReadSource = 2,
    AllocFail = 3,
    TempFile = 4,
    BadCompressionType = 5,
    CabFile = 6,
    UserAbort = 7,
    MciFail = 8,
}

internal static class FciConstants
{
    public const int MaxDiskName = 256;
    public const int MaxCabinetName = 256;
    public const int MaxCabPath = 256;
    public const int MaxFileName = 256;

    // TCOMP (compression type) values from fci.h.
    public const ushort TcompTypeMask = 0x000F;
    public const ushort TcompTypeNone = 0x0000;
    public const ushort TcompTypeMsZip = 0x0001;
    public const ushort TcompTypeLzx = 0x0003;
    public const int TcompShiftLzxWindow = 8;
    public const ushort TcompLzxWindowDefault = 21;

    // PFNFCISTATUS typeStatus values.
    public const uint StatusFile = 0;
    public const uint StatusFolder = 1;
    public const uint StatusCabinet = 2;

    // _O_* flags (msvcrt fcntl.h) used by the open/getopeninfo callbacks.
    public const int ORdOnly = 0x0000;
    public const int OWrOnly = 0x0001;
    public const int ORdWr = 0x0002;
    public const int OCreat = 0x0100;
    public const int OTrunc = 0x0200;
    public const int OBinary = 0x8000;

    public static ushort MakeLzxCompressionType(ushort windowBits = TcompLzxWindowDefault)
        => (ushort)(TcompTypeLzx | (windowBits << TcompShiftLzxWindow));
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr FciAllocDelegate(uint cb);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void FciFreeDelegate(IntPtr memory);

[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal delegate IntPtr FciOpenDelegate(string pszFile, int oflag, int pmode, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint FciReadDelegate(IntPtr hf, IntPtr memory, uint cb, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint FciWriteDelegate(IntPtr hf, IntPtr memory, uint cb, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FciCloseDelegate(IntPtr hf, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FciSeekDelegate(IntPtr hf, int dist, int seektype, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal delegate int FciDeleteDelegate(string pszFile, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate bool FciGetTempFileDelegate(IntPtr pszTempName, int cbTempName, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate bool FciGetNextCabinetDelegate(ref CCAB pccab, uint cbPrevCab, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FciStatusDelegate(uint typeStatus, uint cb1, uint cb2, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal delegate IntPtr FciGetOpenInfoDelegate(string pszName, out ushort pdate, out ushort ptime, out ushort pattribs, out int err, IntPtr pv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal delegate bool FciFilePlacedDelegate(ref CCAB pccab, string pszFile, int cbFile, [MarshalAs(UnmanagedType.Bool)] bool fContinuation, IntPtr pv);
