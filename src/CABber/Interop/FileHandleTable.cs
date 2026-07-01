namespace CABber.Interop;

/// <summary>
/// Maps the opaque <c>IntPtr</c> tokens FCI/FDI pass to the open/read/write/seek/close callbacks
/// to real <see cref="Stream"/> instances. FCI/FDI never dereference these handles themselves —
/// they are just tokens round-tripped back to us — so there is no need for a real Win32 HANDLE.
/// Instance-scoped per build/extract operation, shared by both FCI and FDI contexts.
/// </summary>
internal sealed class FileHandleTable : IDisposable
{
    private readonly Dictionary<IntPtr, Stream> _openStreams = new();
    private long _nextToken = 1;

    public IntPtr Open(Stream stream)
    {
        var token = new IntPtr(_nextToken++);
        _openStreams.Add(token, stream);
        return token;
    }

    public Stream Get(IntPtr token) => _openStreams[token];

    public void Close(IntPtr token)
    {
        if (_openStreams.TryGetValue(token, out var stream))
        {
            _openStreams.Remove(token);
            stream.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var stream in _openStreams.Values)
        {
            stream.Dispose();
        }

        _openStreams.Clear();
    }
}
