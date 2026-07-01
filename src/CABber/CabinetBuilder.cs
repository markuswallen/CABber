#if NET10_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace CABber;

/// <summary>Builds a cabinet (.cab) file via the native FCI (File Compression Interface).</summary>
#if NET10_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class CabinetBuilder : ICabinetBuilder
{
    public CabinetBuilder(CabinetBuilderOptions? options = null)
    {
    }

    public ICabinetBuilder AddFile(string sourceFilePath, string? nameInCabinet = null)
    {
        throw new NotImplementedException();
    }

    public ICabinetBuilder AddDirectory(string sourceDirectoryPath, string? baseNameInCabinet = null, bool recursive = true)
    {
        throw new NotImplementedException();
    }

    public void Build(string cabinetPath)
    {
        throw new NotImplementedException();
    }

    /// <summary>One-shot convenience overload; not part of <see cref="ICabinetBuilder"/> since statics aren't mockable.</summary>
    public static void Build(string cabinetPath, IEnumerable<string> sourceFiles)
    {
        using var builder = new CabinetBuilder();
        foreach (var sourceFile in sourceFiles)
        {
            builder.AddFile(sourceFile);
        }

        builder.Build(cabinetPath);
    }

    public void Dispose()
    {
    }
}
