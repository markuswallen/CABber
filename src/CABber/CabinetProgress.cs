namespace CABber;

/// <summary>Progress information reported during a <see cref="CabinetBuilder"/> build operation.</summary>
public sealed record CabinetProgress(string CurrentFileName, long BytesProcessed, long TotalBytes, int FilesProcessed, int TotalFiles);
