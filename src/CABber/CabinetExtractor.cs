#if NET10_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace CABber;

/// <summary>Lists and extracts files from a cabinet (.cab) file via the native FDI (File Decompression Interface).</summary>
#if NET10_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class CabinetExtractor : ICabinetExtractor
{
    private readonly string _cabinetPath;

    public CabinetExtractor(string cabinetPath)
    {
        if (cabinetPath is null)
        {
            throw new ArgumentNullException(nameof(cabinetPath));
        }

        _cabinetPath = cabinetPath;
    }

    public IReadOnlyList<CabinetEntry> ListFiles()
    {
        throw new NotImplementedException();
    }

    public void ExtractAll(string destinationDirectory, bool overwrite = true)
    {
        throw new NotImplementedException();
    }

    public void ExtractFile(CabinetEntry entry, string destinationDirectory, bool overwrite = true)
    {
        throw new NotImplementedException();
    }

    /// <summary>One-shot convenience overload; not part of <see cref="ICabinetExtractor"/> since statics aren't mockable.</summary>
    public static void ExtractAll(string cabinetPath, string destinationDirectory)
    {
        new CabinetExtractor(cabinetPath).ExtractAll(destinationDirectory);
    }
}
