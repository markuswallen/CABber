using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>Owns the native <c>HFDI</c> handle returned by <c>FDICreate</c> and destroys it via <c>FDIDestroy</c>.</summary>
internal sealed class FdiContextSafeHandle : SafeHandle
{
    public FdiContextSafeHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public void Initialize(IntPtr hfdi) => SetHandle(hfdi);

    protected override bool ReleaseHandle() => NativeMethods.FDIDestroy(handle);
}
