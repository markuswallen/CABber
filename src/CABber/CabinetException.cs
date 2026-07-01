namespace CABber;

/// <summary>
/// Base exception for all cabinet (.cab) creation/extraction failures raised by CABber.
/// </summary>
public class CabinetException : Exception
{
    /// <summary>The native FCI/FDI error code, or 0 if not applicable.</summary>
    public int ErrorCode { get; }

    /// <summary>The native FCI/FDI error type (oper), or 0 if not applicable.</summary>
    public int ErrorType { get; }

    public CabinetException(string message)
        : base(message)
    {
    }

    public CabinetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CabinetException(string message, int errorCode, int errorType)
        : base(message)
    {
        ErrorCode = errorCode;
        ErrorType = errorType;
    }
}

/// <summary>Thrown when a cabinet file is missing or its path cannot be resolved.</summary>
public sealed class CabinetNotFoundException : CabinetException
{
    public CabinetNotFoundException(string message)
        : base(message)
    {
    }

    public CabinetNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CabinetNotFoundException(string message, int errorCode, int errorType)
        : base(message, errorCode, errorType)
    {
    }
}

/// <summary>Thrown when a cabinet file is corrupt or truncated and cannot be parsed.</summary>
public sealed class CabinetCorruptException : CabinetException
{
    public CabinetCorruptException(string message)
        : base(message)
    {
    }

    public CabinetCorruptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CabinetCorruptException(string message, int errorCode, int errorType)
        : base(message, errorCode, errorType)
    {
    }
}

/// <summary>Thrown when an I/O failure occurs while reading or writing cabinet contents.</summary>
public sealed class CabinetIOException : CabinetException
{
    public CabinetIOException(string message)
        : base(message)
    {
    }

    public CabinetIOException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CabinetIOException(string message, int errorCode, int errorType)
        : base(message, errorCode, errorType)
    {
    }
}
