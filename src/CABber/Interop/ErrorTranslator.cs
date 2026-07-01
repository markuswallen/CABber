namespace CABber.Interop;

/// <summary>
/// Single seam every native FCI/FDI failure funnels through: translates an <see cref="ERF"/>
/// (or a raw error code) into the appropriate <see cref="CabinetException"/> subtype.
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

    public static CabinetException FromFdi(string operation, in ERF erf)
    {
        var message = $"{operation} failed (FDI error {erf.erfOper}, type {erf.erfType}).";

        return (FdiErrorCode)erf.erfOper switch
        {
            FdiErrorCode.CabinetNotFound => new CabinetNotFoundException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.NotACabinet => new CabinetCorruptException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.UnknownCabinetVersion => new CabinetCorruptException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.CorruptCabinet => new CabinetCorruptException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.ReserveMismatch => new CabinetCorruptException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.WrongCabinet => new CabinetCorruptException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.TargetFile => new CabinetIOException(message, erf.erfOper, erf.erfType),
            FdiErrorCode.MdiFail => new CabinetIOException(message, erf.erfOper, erf.erfType),
            _ => new CabinetException(message, erf.erfOper, erf.erfType),
        };
    }

    /// <summary>
    /// Wraps a managed exception caught inside a native callback. If the callback already threw a
    /// specific <see cref="CabinetException"/> subtype (e.g. a path-traversal check), that instance is
    /// preserved rather than flattened into the generic base type.
    /// </summary>
    public static CabinetException FromPendingException(string operation, Exception pending)
        => pending as CabinetException ?? new CabinetException($"{operation} failed: {pending.Message}", pending);
}
