using CABber.Interop;

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
        EnsureCabinetExists();

        using var context = new FdiContext(destinationDirectory: null, filter: null, overwrite: false);
        return context.Copy(_cabinetPath);
    }

    public void ExtractAll(string destinationDirectory, bool overwrite = true)
    {
        if (destinationDirectory is null)
        {
            throw new ArgumentNullException(nameof(destinationDirectory));
        }

        EnsureCabinetExists();
        Directory.CreateDirectory(destinationDirectory);

        using var context = new FdiContext(destinationDirectory, filter: null, overwrite);
        context.Copy(_cabinetPath);
    }

    public void ExtractFile(CabinetEntry entry, string destinationDirectory, bool overwrite = true)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (destinationDirectory is null)
        {
            throw new ArgumentNullException(nameof(destinationDirectory));
        }

        EnsureCabinetExists();
        Directory.CreateDirectory(destinationDirectory);

        using var context = new FdiContext(
            destinationDirectory,
            filter: name => string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase),
            overwrite);

        var found = context.Copy(_cabinetPath).Any(e => string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

        if (!found)
        {
            throw new CabinetException($"Entry '{entry.Name}' was not found in cabinet '{_cabinetPath}'.");
        }
    }

    /// <summary>One-shot convenience overload; not part of <see cref="ICabinetExtractor"/> since statics aren't mockable.</summary>
    public static void ExtractAll(string cabinetPath, string destinationDirectory)
    {
        new CabinetExtractor(cabinetPath).ExtractAll(destinationDirectory);
    }

    private void EnsureCabinetExists()
    {
        if (!File.Exists(_cabinetPath))
        {
            throw new CabinetNotFoundException($"Cabinet file not found: {_cabinetPath}");
        }
    }
}
