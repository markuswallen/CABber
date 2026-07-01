using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>
/// Owns a native FDI (extract) session: the <c>HFDI</c> handle, the callback delegates FDI invokes
/// while decompressing, and the per-operation <see cref="FileHandleTable"/>. One instance drives
/// exactly one <c>FDICopy</c> pass over a single cabinet file.
///
/// The same notification pass serves both listing and extracting: when <see cref="_destinationDirectory"/>
/// is <c>null</c> the <see cref="FdiNotificationType.CopyFile"/> handler returns <see cref="IntPtr.Zero"/>
/// for every entry (skip — metadata is still recorded), collecting <see cref="CabinetEntry"/> values
/// without writing anything to disk. An optional <see cref="_filter"/> narrows extraction to a single
/// entry (used by <c>CabinetExtractor.ExtractFile</c>) while metadata for every entry is still collected.
///
/// As with <see cref="FciContext"/>, every callback delegate is rooted as an instance field for the
/// lifetime of the native handle to avoid a GC collecting a delegate cabinet.dll still holds a raw
/// function pointer to.
/// </summary>
internal sealed class FdiContext : IDisposable
{
    private readonly FdiContextSafeHandle _handle;
    private readonly FileHandleTable _fileHandles = new();
    private readonly Dictionary<IntPtr, string> _openDestinationPaths = new();
    private readonly List<CabinetEntry> _entries = new();
    private readonly string? _destinationDirectory;
    private readonly Func<string, bool>? _filter;
    private readonly bool _overwrite;
    private readonly IntPtr _erfPtr;

    private readonly FdiAllocDelegate _allocDelegate;
    private readonly FdiFreeDelegate _freeDelegate;
    private readonly FdiOpenDelegate _openDelegate;
    private readonly FdiReadDelegate _readDelegate;
    private readonly FdiWriteDelegate _writeDelegate;
    private readonly FdiCloseDelegate _closeDelegate;
    private readonly FdiSeekDelegate _seekDelegate;
    private readonly FdiNotifyDelegate _notifyDelegate;

    private Exception? _pendingException;
    private bool _disposed;

    /// <param name="destinationDirectory">
    /// Destination root for extracted files, or <c>null</c> to only list entries without writing anything.
    /// </param>
    /// <param name="filter">
    /// When non-null, only entries whose cabinet-relative name this predicate accepts are extracted;
    /// every entry is still recorded in the returned metadata regardless of the filter.
    /// </param>
    public FdiContext(string? destinationDirectory, Func<string, bool>? filter, bool overwrite)
    {
        _destinationDirectory = destinationDirectory;
        _filter = filter;
        _overwrite = overwrite;

        _allocDelegate = Alloc;
        _freeDelegate = Free;
        _openDelegate = Open;
        _readDelegate = Read;
        _writeDelegate = Write;
        _closeDelegate = Close;
        _seekDelegate = Seek;
        _notifyDelegate = OnNotify;

        _erfPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ERF>());
        Marshal.StructureToPtr(default(ERF), _erfPtr, false);

        var hfdi = NativeMethods.FDICreate(
            _allocDelegate,
            _freeDelegate,
            _openDelegate,
            _readDelegate,
            _writeDelegate,
            _closeDelegate,
            _seekDelegate,
            FdiConstants.CpuUnknown,
            _erfPtr);

        if (hfdi == IntPtr.Zero)
        {
            var erf = ReadErf();
            Marshal.FreeHGlobal(_erfPtr);
            throw _pendingException is not null
                ? ErrorTranslator.FromPendingException("FDICreate", _pendingException)
                : ErrorTranslator.FromFdi("FDICreate", erf);
        }

        _handle = new FdiContextSafeHandle();
        _handle.Initialize(hfdi);
    }

    /// <summary>Runs a single FDICopy pass over <paramref name="cabinetPath"/>, returning metadata for every entry it contains.</summary>
    public IReadOnlyList<CabinetEntry> Copy(string cabinetPath)
    {
        _pendingException = null;
        _entries.Clear();

        var fullPath = Path.GetFullPath(cabinetPath);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);

        var cabPath = string.IsNullOrEmpty(directory)
            ? "." + Path.DirectorySeparatorChar
            : directory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        ValidateIsCabinet(fullPath);

        var ok = NativeMethods.FDICopy(
            _handle.DangerousGetHandle(),
            fileName,
            cabPath,
            flags: 0,
            _notifyDelegate,
            pfnfdid: IntPtr.Zero,
            pvUser: IntPtr.Zero);

        // FDICopy's own return value isn't a reliable failure signal here: for fdintCLOSE_FILE_INFO
        // the callback's "abort" sentinel is FALSE (0), which is numerically indistinguishable from a
        // successful IntPtr(0) elsewhere, so an exception recorded during that notification must also
        // be checked explicitly rather than trusting `ok` alone.
        if (!ok || _pendingException is not null)
        {
            throw TranslateFailure($"FDICopy('{fileName}')");
        }

        return _entries;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
        _fileHandles.Dispose();
        Marshal.FreeHGlobal(_erfPtr);
    }

    /// <summary>
    /// Fails fast with a clear, translated exception (missing / not-a-cabinet / corrupt) before running
    /// the full FDICopy notification pass, rather than only surfacing the problem deep inside a callback.
    /// </summary>
    private void ValidateIsCabinet(string fullPath)
    {
        var token = Open(fullPath, oflag: 0, pmode: 0);
        if (token == new IntPtr(-1))
        {
            throw TranslateFailure($"FDIIsCabinet('{fullPath}')");
        }

        try
        {
            var info = default(FDICABINETINFO);
            var ok = NativeMethods.FDIIsCabinet(_handle.DangerousGetHandle(), token, ref info);
            if (!ok)
            {
                throw TranslateFailure($"FDIIsCabinet('{fullPath}')");
            }
        }
        finally
        {
            _fileHandles.Close(token);
        }
    }

    private ERF ReadErf() => Marshal.PtrToStructure<ERF>(_erfPtr);

    private CabinetException TranslateFailure(string operation) => _pendingException is not null
        ? ErrorTranslator.FromPendingException(operation, _pendingException)
        : ErrorTranslator.FromFdi(operation, ReadErf());

    private IntPtr Alloc(uint cb) => Marshal.AllocHGlobal((int)cb);

    private void Free(IntPtr memory) => Marshal.FreeHGlobal(memory);

    /// <summary>Opens the cabinet source file (or a subsequent spanned cabinet); never used for destination files.</summary>
    private IntPtr Open(string pszFile, int oflag, int pmode)
    {
        try
        {
            if (!File.Exists(pszFile))
            {
                _pendingException = new CabinetNotFoundException($"Cabinet file not found: {pszFile}");
                return new IntPtr(-1);
            }

            var stream = new FileStream(pszFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            return _fileHandles.Open(stream);
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return new IntPtr(-1);
        }
    }

    private uint Read(IntPtr hf, IntPtr memory, uint cb)
    {
        try
        {
            var stream = _fileHandles.Get(hf);
            var buffer = new byte[cb];
            var read = stream.Read(buffer, 0, (int)cb);
            Marshal.Copy(buffer, 0, memory, read);
            return (uint)read;
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return unchecked((uint)-1);
        }
    }

    private uint Write(IntPtr hf, IntPtr memory, uint cb)
    {
        try
        {
            var stream = _fileHandles.Get(hf);
            var buffer = new byte[cb];
            Marshal.Copy(memory, buffer, 0, (int)cb);
            stream.Write(buffer, 0, (int)cb);
            return cb;
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return unchecked((uint)-1);
        }
    }

    private int Close(IntPtr hf)
    {
        try
        {
            _fileHandles.Close(hf);
            return 0;
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return -1;
        }
    }

    private int Seek(IntPtr hf, int dist, int seektype)
    {
        try
        {
            var stream = _fileHandles.Get(hf);
            var origin = seektype switch
            {
                0 => SeekOrigin.Begin,
                1 => SeekOrigin.Current,
                2 => SeekOrigin.End,
                _ => SeekOrigin.Begin,
            };

            return (int)stream.Seek(dist, origin);
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return -1;
        }
    }

    private IntPtr OnNotify(FdiNotificationType fdint, ref FDINOTIFICATION pfdin)
    {
        try
        {
            return fdint switch
            {
                FdiNotificationType.CopyFile => OnCopyFile(ref pfdin),
                FdiNotificationType.CloseFileInfo => OnCloseFileInfo(ref pfdin),
                FdiNotificationType.NextCabinet => OnNextCabinet(),
                _ => IntPtr.Zero,
            };
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return new IntPtr(-1);
        }
    }

    private IntPtr OnCopyFile(ref FDINOTIFICATION pfdin)
    {
        var nameInCabinet = Marshal.PtrToStringAnsi(pfdin.psz1) ?? string.Empty;
        var lastWriteTime = FromDosDateTime(pfdin.date, pfdin.time);
        var attributes = (FileAttributes)(pfdin.attribs & 0x27); // ReadOnly | Hidden | System | Archive
        _entries.Add(new CabinetEntry(nameInCabinet, lastWriteTime, attributes, pfdin.cb));

        var shouldExtract = _destinationDirectory is not null && (_filter is null || _filter(nameInCabinet));
        if (!shouldExtract)
        {
            return IntPtr.Zero;
        }

        string destinationPath;
        FileStream stream;
        try
        {
            destinationPath = ResolveDestinationPath(_destinationDirectory!, nameInCabinet);

            if (!_overwrite && File.Exists(destinationPath))
            {
                _pendingException = new IOException($"Destination file already exists: {destinationPath}");
                return new IntPtr(-1);
            }

            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir!);
            }

            stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            // nameInCabinet is read straight out of the cabinet's CFFILE record. A path the OS/.NET
            // itself rejects as syntactically invalid while resolving or opening it is strong evidence
            // that record is corrupt, not a legitimate I/O failure — classify it as such rather than
            // letting a raw ArgumentException/IOException surface as the generic base exception type.
            // (CabinetException itself doesn't match this filter, so ResolveDestinationPath's own
            // path-traversal exception still passes through unaltered.)
            throw new CabinetCorruptException(
                $"Cabinet entry '{nameInCabinet}' resolved to an invalid destination path, indicating a corrupt cabinet: {ex.Message}",
                ex);
        }

        var token = _fileHandles.Open(stream);
        _openDestinationPaths[token] = destinationPath;
        return token;
    }

    /// <summary>
    /// Unlike every other notification, FDI's "abort" sentinel for CLOSE_FILE_INFO is FALSE (0) —
    /// numerically indistinguishable from the "continue" value used elsewhere (e.g. IntPtr.Zero also
    /// means "skip" for COPY_FILE) — so this needs its own try/catch rather than sharing
    /// <see cref="OnNotify"/>'s generic one, which returns -1 (nonzero, i.e. "continue" here) on error.
    /// </summary>
    private IntPtr OnCloseFileInfo(ref FDINOTIFICATION pfdin)
    {
        try
        {
            if (_openDestinationPaths.TryGetValue(pfdin.hf, out var destinationPath))
            {
                _openDestinationPaths.Remove(pfdin.hf);
                _fileHandles.Close(pfdin.hf);

                File.SetLastWriteTime(destinationPath, FromDosDateTime(pfdin.date, pfdin.time));

                var attributes = (FileAttributes)(pfdin.attribs & 0x27);
                if (attributes != 0)
                {
                    File.SetAttributes(destinationPath, attributes);
                }
            }

            return new IntPtr(1); // TRUE: continue processing.
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return IntPtr.Zero; // FALSE: abort.
        }
    }

    private IntPtr OnNextCabinet()
    {
        _pendingException = new NotSupportedException(
            "The cabinet spans multiple physical cabinet files; extracting spanned cabinets is not supported.");
        return new IntPtr(-1);
    }

    /// <summary>
    /// Resolves a cabinet-stored entry name against <paramref name="destinationDirectory"/>, rejecting
    /// any entry that would escape it — a zip-slip-style path-traversal guard. Belt-and-suspenders:
    /// "." / ".." segments are rejected explicitly, and the fully resolved path is re-checked to fall
    /// under the destination root, in case some other trick (absolute path segment, drive letter, etc.)
    /// slipped past the segment check.
    /// </summary>
    private static string ResolveDestinationPath(string destinationDirectory, string nameInCabinet)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var segments = nameInCabinet.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new CabinetException(
                $"Cabinet entry '{nameInCabinet}' contains a path-traversal segment and cannot be extracted safely.");
        }

        var combinedPath = Path.GetFullPath(Path.Combine(new[] { destinationRoot }.Concat(segments).ToArray()));
        var destinationRootWithSeparator = destinationRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;

        if (!combinedPath.StartsWith(destinationRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new CabinetException(
                $"Cabinet entry '{nameInCabinet}' would extract outside the destination directory.");
        }

        return combinedPath;
    }

    private static DateTime FromDosDateTime(ushort date, ushort time)
    {
        var year = 1980 + (date >> 9);
        var month = (date >> 5) & 0xF;
        var day = date & 0x1F;
        var hour = (time >> 11) & 0x1F;
        var minute = (time >> 5) & 0x3F;
        var second = (time & 0x1F) * 2;

        try
        {
            return new DateTime(year, Math.Max(1, month), Math.Max(1, day), hour, minute, Math.Min(59, second));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }
}
