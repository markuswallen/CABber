using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>P/Invoke entry points exported by <c>cabinet.dll</c> for the FCI (create) API.</summary>
internal static class NativeMethods
{
    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern IntPtr FCICreate(
        IntPtr perf,
        FciFilePlacedDelegate pfnfiledest,
        FciAllocDelegate pfnalloc,
        FciFreeDelegate pfnfree,
        FciOpenDelegate pfnopen,
        FciReadDelegate pfnread,
        FciWriteDelegate pfnwrite,
        FciCloseDelegate pfnclose,
        FciSeekDelegate pfnseek,
        FciDeleteDelegate pfndelete,
        FciGetTempFileDelegate pfnfcigtf,
        ref CCAB pccab,
        IntPtr pv);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FCIAddFile(
        IntPtr hfci,
        string pszSourceFile,
        string pszFileName,
        [MarshalAs(UnmanagedType.Bool)] bool fExecute,
        FciGetNextCabinetDelegate pfnGetNextCabinet,
        FciStatusDelegate pfnStatus,
        FciGetOpenInfoDelegate pfnGetOpenInfo,
        ushort typeCompress);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FCIFlushCabinet(
        IntPtr hfci,
        [MarshalAs(UnmanagedType.Bool)] bool fGetNextCab,
        FciGetNextCabinetDelegate pfnGetNextCabinet,
        FciStatusDelegate pfnStatus);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FCIFlushFolder(
        IntPtr hfci,
        FciGetNextCabinetDelegate pfnGetNextCabinet,
        FciStatusDelegate pfnStatus);

    [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FCIDestroy(IntPtr hfci);
}
