namespace CABber;

/// <summary>Builds a cabinet (.cab) file from source files and directories.</summary>
public interface ICabinetBuilder : IDisposable
{
    ICabinetBuilder AddFile(string sourceFilePath, string? nameInCabinet = null);

    ICabinetBuilder AddDirectory(string sourceDirectoryPath, string? baseNameInCabinet = null, bool recursive = true);

    void Build(string cabinetPath);
}
