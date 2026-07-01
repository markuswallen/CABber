namespace CABber.Interop;

/// <summary>
/// Single seam every native FCI/FDI failure funnels through: translates an <see cref="ERF"/>
/// (or a raw error code) into the appropriate <see cref="CabinetException"/> subtype.
/// FDI extends this in stage 3 for FDIERROR codes.
/// </summary>
internal static class ErrorTranslator
{
    public static CabinetException FromFci(string operation, in ERF erf)
    {
        var message = $"{operation} failed (FCI error {erf.erfOper}, type {erf.erfType}).";

        return (FciErrorCode)erf.erfOper switch
        {
            FciErrorCode.OpenSource => new CabinetNotFoundException(message, erf.erfOper, erf.erfType),
            FciErrorCode.ReadSource => new CabinetIOException(message, erf.erfOper, erf.erfType),
            FciErrorCode.TempFile => new CabinetIOException(message, erf.erfOper, erf.erfType),
            FciErrorCode.CabFile => new CabinetIOException(message, erf.erfOper, erf.erfType),
            FciErrorCode.MciFail => new CabinetIOException(message, erf.erfOper, erf.erfType),
            _ => new CabinetException(message, erf.erfOper, erf.erfType),
        };
    }

    public static CabinetException FromPendingException(string operation, Exception pending)
        => new CabinetException($"{operation} failed: {pending.Message}", pending);
}
