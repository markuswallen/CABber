using System.Runtime.InteropServices;
using CABber.Interop;

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
    private readonly CabinetBuilderOptions _options;
    private readonly List<(string SourcePath, string NameInCabinet)> _entries = new();
    private bool _built;
    private bool _disposed;

    public CabinetBuilder(CabinetBuilderOptions? options = null)
    {
        _options = options ?? new CabinetBuilderOptions();
    }

    public ICabinetBuilder AddFile(string sourceFilePath, string? nameInCabinet = null)
    {
        if (sourceFilePath is null)
        {
            throw new ArgumentNullException(nameof(sourceFilePath));
        }

        ThrowIfDisposed();

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}", sourceFilePath);
        }

        var name = nameInCabinet ?? Path.GetFileName(sourceFilePath);
        ValidateNameInCabinet(name, nameof(nameInCabinet));

        _entries.Add((sourceFilePath, name));
        return this;
    }

    public ICabinetBuilder AddDirectory(string sourceDirectoryPath, string? baseNameInCabinet = null, bool recursive = true)
    {
        if (sourceDirectoryPath is null)
        {
            throw new ArgumentNullException(nameof(sourceDirectoryPath));
        }

        ThrowIfDisposed();

        if (!Directory.Exists(sourceDirectoryPath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectoryPath}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var basePrefix = string.IsNullOrEmpty(baseNameInCabinet) ? null : baseNameInCabinet!.TrimEnd('\\', '/');

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", searchOption))
        {
            var relative = GetRelativePath(sourceDirectoryPath, filePath).Replace('/', '\\');
            var nameInCabinet = basePrefix is null ? relative : $"{basePrefix}\\{relative}";
            AddFile(filePath, nameInCabinet);
        }

        return this;
    }

    public void Build(string cabinetPath)
    {
        if (cabinetPath is null)
        {
            throw new ArgumentNullException(nameof(cabinetPath));
        }

        ThrowIfDisposed();

        if (_built)
        {
            throw new InvalidOperationException("This CabinetBuilder has already built a cabinet. Create a new instance to build another.");
        }

        var fullPath = Path.GetFullPath(cabinetPath);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);

        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("Cabinet path must include a file name.", nameof(cabinetPath));
        }

        ValidateAnsiSafe(fileName, nameof(cabinetPath));
        if (AnsiByteCount(fileName) >= FciConstants.MaxCabinetName)
        {
            throw new CabinetException($"Cabinet file name '{fileName}' is too long for cabinet.dll's ANSI buffer.");
        }

        var cabPath = string.IsNullOrEmpty(directory)
            ? "." + Path.DirectorySeparatorChar
            : directory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        ValidateAnsiSafe(cabPath, nameof(cabinetPath));
        if (AnsiByteCount(cabPath) >= FciConstants.MaxCabPath)
        {
            throw new CabinetException($"Cabinet directory path '{cabPath}' is too long for cabinet.dll's ANSI buffer.");
        }

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        var ccab = new CCAB
        {
            cb = (int)Math.Min(_options.MaxCabinetSize, int.MaxValue),
            cbFolderThresh = 0,
            iCab = 1,
            iDisk = 0,
            fFailOnIncompressible = 0,
            setID = 0,
            szDisk = string.Empty,
            szCab = fileName,
            szCabPath = cabPath,
        };

        var totalBytes = _entries.Sum(entry => new FileInfo(entry.SourcePath).Length);

        using var context = new FciContext(ccab, _options.Compression, _options.Progress, _entries.Count, totalBytes);

        foreach (var (sourcePath, nameInCabinet) in _entries)
        {
            context.AddFile(sourcePath, nameInCabinet);
        }

        context.FlushCabinet();
        _built = true;
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
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CabinetBuilder));
        }
    }

    private static void ValidateNameInCabinet(string name, string paramName)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The name stored in the cabinet must not be empty.", paramName);
        }

        ValidateAnsiSafe(name, paramName);

        if (AnsiByteCount(name) >= FciConstants.MaxFileName)
        {
            throw new CabinetException($"'{name}' is too long for cabinet.dll's ANSI buffer.");
        }
    }

    private static void ValidateAnsiSafe(string value, string paramName)
    {
        foreach (var ch in value)
        {
            if (ch > 0xFF)
            {
                throw new CabinetException(
                    $"'{value}' contains characters that cannot be represented in the ANSI code page cabinet.dll requires (parameter '{paramName}').");
            }
        }
    }

    private static int AnsiByteCount(string value)
    {
        var ptr = Marshal.StringToHGlobalAnsi(value);
        try
        {
            var length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
            {
                length++;
            }

            return length;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string GetRelativePath(string baseDirectory, string fullPath)
    {
        var baseFull = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFull = Path.GetFullPath(fullPath);

        if (targetFull.Length > baseFull.Length + 1 && targetFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
        {
            return targetFull.Substring(baseFull.Length + 1);
        }

        return targetFull;
    }
}
