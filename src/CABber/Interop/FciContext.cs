using System.Runtime.InteropServices;

namespace CABber.Interop;

/// <summary>
/// Owns a native FCI (create) session: the <c>HFCI</c> handle, the callback delegates FCI invokes
/// during compression, and the per-operation <see cref="FileHandleTable"/>.
///
/// Two correctness-critical points:
/// 1. Every callback delegate is stored as an instance field so it stays rooted for as long as the
///    native handle is alive — if the GC collected a callback between calls, cabinet.dll would be
///    left holding a dangling function pointer.
/// 2. The <see cref="ERF"/> FCI uses to report errors is retained by the native side across calls
///    (supplied once to <c>FCICreate</c>, not resupplied per call), and ERF is fully blittable —
///    passing it as a managed <c>ref</c> would only pin it for the duration of the creation call,
///    after which a compacting GC could relocate it and leave cabinet.dll with a dangling pointer.
///    It is allocated with <see cref="Marshal.AllocHGlobal(int)"/> instead, which the GC never moves.
/// </summary>
internal sealed class FciContext : IDisposable
{
    private readonly FciContextSafeHandle _handle;
    private readonly FileHandleTable _fileHandles = new();
    private readonly IProgress<CabinetProgress>? _progress;
    private readonly int _totalFiles;
    private readonly long _totalBytes;
    private readonly CompressionType _compression;
    private readonly IntPtr _erfPtr;

    private readonly FciAllocDelegate _allocDelegate;
    private readonly FciFreeDelegate _freeDelegate;
    private readonly FciOpenDelegate _openDelegate;
    private readonly FciReadDelegate _readDelegate;
    private readonly FciWriteDelegate _writeDelegate;
    private readonly FciCloseDelegate _closeDelegate;
    private readonly FciSeekDelegate _seekDelegate;
    private readonly FciDeleteDelegate _deleteDelegate;
    private readonly FciGetTempFileDelegate _getTempFileDelegate;
    private readonly FciGetNextCabinetDelegate _getNextCabinetDelegate;
    private readonly FciStatusDelegate _statusDelegate;
    private readonly FciGetOpenInfoDelegate _getOpenInfoDelegate;
    private readonly FciFilePlacedDelegate _filePlacedDelegate;

    private Exception? _pendingException;
    private long _bytesCompletedBeforeCurrentFile;
    private int _filesProcessed;
    private bool _disposed;

    public FciContext(CCAB ccab, CompressionType compression, IProgress<CabinetProgress>? progress, int totalFiles, long totalBytes)
    {
        _compression = compression;
        _progress = progress;
        _totalFiles = totalFiles;
        _totalBytes = totalBytes;

        _allocDelegate = Alloc;
        _freeDelegate = Free;
        _openDelegate = Open;
        _readDelegate = Read;
        _writeDelegate = Write;
        _closeDelegate = Close;
        _seekDelegate = Seek;
        _deleteDelegate = Delete;
        _getTempFileDelegate = GetTempFile;
        _getNextCabinetDelegate = GetNextCabinet;
        _statusDelegate = Status;
        _getOpenInfoDelegate = GetOpenInfo;
        _filePlacedDelegate = FilePlaced;

        _erfPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ERF>());
        Marshal.StructureToPtr(default(ERF), _erfPtr, false);

        var hfci = NativeMethods.FCICreate(
            _erfPtr,
            _filePlacedDelegate,
            _allocDelegate,
            _freeDelegate,
            _openDelegate,
            _readDelegate,
            _writeDelegate,
            _closeDelegate,
            _seekDelegate,
            _deleteDelegate,
            _getTempFileDelegate,
            ref ccab,
            IntPtr.Zero);

        if (hfci == IntPtr.Zero)
        {
            var erf = ReadErf();
            Marshal.FreeHGlobal(_erfPtr);
            throw _pendingException is not null
                ? ErrorTranslator.FromPendingException("FCICreate", _pendingException)
                : ErrorTranslator.FromFci("FCICreate", erf);
        }

        _handle = new FciContextSafeHandle();
        _handle.Initialize(hfci);
    }

    public void AddFile(string sourceFilePath, string nameInCabinet)
    {
        _pendingException = null;

        var ok = NativeMethods.FCIAddFile(
            _handle.DangerousGetHandle(),
            sourceFilePath,
            nameInCabinet,
            fExecute: false,
            _getNextCabinetDelegate,
            _statusDelegate,
            _getOpenInfoDelegate,
            CompressionTypeToTComp(_compression));

        if (!ok)
        {
            throw TranslateFailure($"AddFile('{nameInCabinet}')");
        }
    }

    public void FlushCabinet()
    {
        _pendingException = null;

        var ok = NativeMethods.FCIFlushCabinet(_handle.DangerousGetHandle(), fGetNextCab: false, _getNextCabinetDelegate, _statusDelegate);

        if (!ok)
        {
            throw TranslateFailure("FlushCabinet");
        }
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

    private static ushort CompressionTypeToTComp(CompressionType compression) => compression switch
    {
        CompressionType.None => FciConstants.TcompTypeNone,
        CompressionType.MsZip => FciConstants.TcompTypeMsZip,
        CompressionType.Lzx => FciConstants.MakeLzxCompressionType(),
        _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, message: null),
    };

    private ERF ReadErf() => Marshal.PtrToStructure<ERF>(_erfPtr);

    private CabinetException TranslateFailure(string operation) => _pendingException is not null
        ? ErrorTranslator.FromPendingException(operation, _pendingException)
        : ErrorTranslator.FromFci(operation, ReadErf());

    private IntPtr Alloc(uint cb) => Marshal.AllocHGlobal((int)cb);

    private void Free(IntPtr memory) => Marshal.FreeHGlobal(memory);

    private IntPtr Open(string pszFile, int oflag, int pmode, out int err, IntPtr pv)
    {
        err = 0;
        try
        {
            FileMode mode;
            FileAccess access;
            if ((oflag & FciConstants.OCreat) != 0)
            {
                mode = FileMode.Create;
                access = FileAccess.ReadWrite;
            }
            else
            {
                mode = FileMode.Open;
                access = (oflag & FciConstants.ORdWr) != 0 ? FileAccess.ReadWrite : FileAccess.Read;
            }

            var stream = new FileStream(pszFile, mode, access, FileShare.Read);
            return _fileHandles.Open(stream);
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            err = 1;
            return new IntPtr(-1);
        }
    }

    private uint Read(IntPtr hf, IntPtr memory, uint cb, out int err, IntPtr pv)
    {
        err = 0;
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
            err = 1;
            return unchecked((uint)-1);
        }
    }

    private uint Write(IntPtr hf, IntPtr memory, uint cb, out int err, IntPtr pv)
    {
        err = 0;
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
            err = 1;
            return unchecked((uint)-1);
        }
    }

    private int Close(IntPtr hf, out int err, IntPtr pv)
    {
        err = 0;
        try
        {
            _fileHandles.Close(hf);
            return 0;
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            err = 1;
            return -1;
        }
    }

    private int Seek(IntPtr hf, int dist, int seektype, out int err, IntPtr pv)
    {
        err = 0;
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
            err = 1;
            return -1;
        }
    }

    private int Delete(string pszFile, out int err, IntPtr pv)
    {
        err = 0;
        try
        {
            File.Delete(pszFile);
            return 0;
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            err = 1;
            return -1;
        }
    }

    private bool GetTempFile(IntPtr pszTempName, int cbTempName, IntPtr pv)
    {
        var ansiPtr = IntPtr.Zero;
        try
        {
            var tempPath = Path.GetTempFileName();
            ansiPtr = Marshal.StringToHGlobalAnsi(tempPath);

            var length = 0;
            while (Marshal.ReadByte(ansiPtr, length) != 0)
            {
                length++;
            }

            length++; // include the null terminator

            if (length > cbTempName)
            {
                throw new IOException($"Temporary file path '{tempPath}' does not fit in the {cbTempName}-byte FCI buffer.");
            }

            var bytes = new byte[length];
            Marshal.Copy(ansiPtr, bytes, 0, length);
            Marshal.Copy(bytes, 0, pszTempName, length);
            return true;
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            return false;
        }
        finally
        {
            if (ansiPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ansiPtr);
            }
        }
    }

    private bool GetNextCabinet(ref CCAB pccab, uint cbPrevCab, IntPtr pv)
    {
        _pendingException = new NotSupportedException(
            "The cabinet content exceeds CabinetBuilderOptions.MaxCabinetSize; spanning across multiple physical cabinet files is not supported.");
        return false;
    }

    /// <summary>
    /// Reports incremental compression byte-progress; <c>statusFile</c> notifications are not
    /// guaranteed one-per-file (FCI can coalesce small files into a single compressed block
    /// before flushing), so per-file accounting happens in <see cref="FilePlaced"/> instead.
    /// </summary>
    private int Status(uint typeStatus, uint cb1, uint cb2, IntPtr pv) => 0;

    private IntPtr GetOpenInfo(string pszName, out ushort pdate, out ushort ptime, out ushort pattribs, out int err, IntPtr pv)
    {
        err = 0;
        try
        {
            var info = new FileInfo(pszName);
            ToDosDateTime(info.LastWriteTime, out pdate, out ptime);
            pattribs = (ushort)((int)info.Attributes & 0xFF);

            var stream = new FileStream(pszName, FileMode.Open, FileAccess.Read, FileShare.Read);
            return _fileHandles.Open(stream);
        }
        catch (Exception ex)
        {
            _pendingException = ex;
            err = 1;
            pdate = 0;
            ptime = 0;
            pattribs = 0;
            return new IntPtr(-1);
        }
    }

    /// <summary>
    /// Called exactly once per file, once FCI has fully placed it into the current cabinet —
    /// the reliable per-file completion signal (unlike <see cref="Status"/>'s <c>statusFile</c>
    /// notifications, which track raw compression byte-progress).
    /// </summary>
    private bool FilePlaced(ref CCAB pccab, string pszFile, int cbFile, bool fContinuation, IntPtr pv)
    {
        _filesProcessed++;
        _bytesCompletedBeforeCurrentFile += cbFile;
        _progress?.Report(new CabinetProgress(pszFile, _bytesCompletedBeforeCurrentFile, _totalBytes, _filesProcessed, _totalFiles));
        return true;
    }

    private static void ToDosDateTime(DateTime dateTime, out ushort date, out ushort time)
    {
        if (dateTime.Year < 1980)
        {
            dateTime = new DateTime(1980, 1, 1);
        }

        date = (ushort)(((dateTime.Year - 1980) << 9) | (dateTime.Month << 5) | dateTime.Day);
        time = (ushort)((dateTime.Hour << 11) | (dateTime.Minute << 5) | (dateTime.Second / 2));
    }
}
