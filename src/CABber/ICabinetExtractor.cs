namespace CABber;

/// <summary>Lists and extracts files from a cabinet (.cab) file.</summary>
public interface ICabinetExtractor
{
    IReadOnlyList<CabinetEntry> ListFiles();

    void ExtractAll(string destinationDirectory, bool overwrite = true);

    void ExtractFile(CabinetEntry entry, string destinationDirectory, bool overwrite = true);
}
