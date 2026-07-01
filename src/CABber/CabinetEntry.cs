namespace CABber;

/// <summary>Metadata for a single file stored inside a cabinet.</summary>
public sealed record CabinetEntry(string Name, DateTime LastWriteTime, FileAttributes Attributes, long UncompressedSize);
