using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>Owns the native <c>HFCI</c> handle returned by <c>FCICreate</c> and destroys it via <c>FCIDestroy</c>.</summary>
internal sealed class FciContextSafeHandle : SafeHandle
{
    public FciContextSafeHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public void Initialize(IntPtr hfci) => SetHandle(hfci);

    protected override bool ReleaseHandle() => NativeMethods.FCIDestroy(handle);
}
