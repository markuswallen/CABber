namespace CABber;

/// <summary>Options controlling how <see cref="CabinetBuilder"/> builds a cabinet.</summary>
public sealed class CabinetBuilderOptions
{
    public long MaxCabinetSize { get; init; } = int.MaxValue;

    public CompressionType Compression { get; init; } = CompressionType.MsZip;

    public IProgress<CabinetProgress>? Progress { get; init; }
}
