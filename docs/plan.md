# CABber v1: FCI/FDI cabinet library

## Context

CABber is a .NET wrapper around the Windows cabinet (.cab) creation/extraction APIs. This is a greenfield project — v1 establishes the project scaffolding plus a full create/extract implementation.

Design decisions:
1. **Scope**: full round-trip — create *and* extract `.cab` files.
2. **Approach**: P/Invoke directly against the native FCI (File Compression Interface) and FDI (File Decompression Interface) exported from `cabinet.dll`, not shelling out to `makecab.exe`/`expand.exe`.
3. **Targets**: multi-target `net10.0` + `netstandard2.0` for broad consumer compatibility. `net10.0` is the current LTS (supported into ~2028); `net8.0` was considered but dropped since it's only months from end of support and the interop layer doesn't use anything net8-specific (see below), so it would add build/test matrix cost with no offsetting benefit. `netstandard2.0` already covers the "older consumer" (.NET Framework 4.6.1+) case. The library is inherently Windows-only, so it's annotated `[SupportedOSPlatform("windows")]`.
4. **Sync only, no async API**: FCI/FDI are blocking, callback-driven native calls — there's no real overlapped I/O to expose, so a `BuildAsync`/`ExtractAllAsync` would just be `Task.Run` wrapping around the sync call. Deliberately not adding that surface in v1; callers who want non-blocking behavior can wrap with `Task.Run` themselves.
5. **Interfaces for mockability**: the public builder/extractor types are exposed as interfaces (`ICabinetBuilder`, `ICabinetExtractor`, `ICabinetExtractorFactory`) so consumers can mock CABber in their own tests without touching real cabinet files.

## Project layout

```
CABber/
├── CABber.sln
├── Directory.Build.props
├── src/CABber/
│   ├── CABber.csproj
│   ├── CabinetBuilder.cs
│   ├── CabinetExtractor.cs
│   ├── CabinetEntry.cs
│   ├── CabinetException.cs
│   └── Interop/
│       ├── Fci.cs                  (CCAB, ERF structs; FCI delegate types; constants)
│       ├── Fdi.cs                  (FDINOTIFICATION struct; FDI delegate types; constants)
│       ├── NativeMethods.cs        (DllImport("cabinet.dll") entry points, FCI+FDI)
│       ├── FciContext.cs           (owns FCICreate handle, rooted delegates, AddFile/Flush)
│       ├── FdiContext.cs           (owns FDICreate handle, rooted delegates, notify callback)
│       ├── FciContextSafeHandle.cs / FdiContextSafeHandle.cs
│       ├── FileHandleTable.cs      (opaque IntPtr -> Stream map, shared by FCI+FDI)
│       └── ErrorTranslator.cs      (ERF/FDIERROR codes -> CabinetException subtypes)
└── tests/CABber.Tests/
    ├── CABber.Tests.csproj
    ├── CabinetRoundTripTests.cs
    ├── CabinetBuilderTests.cs
    ├── CabinetExtractorTests.cs
    ├── CabinetExceptionTests.cs
    └── TestFixtures/  (small valid .cab, one built via makecab.exe as cross-oracle, one corrupted .cab)
```

`CABber.csproj` (src): `TargetFrameworks=net10.0;netstandard2.0`, `SupportedPlatform=windows` (net10.0 TFM), `AllowUnsafeBlocks=true`, NuGet metadata wired now (`PackageId=CABber`, `Version=0.1.0`, MIT license expression, description, repo URL) but `GeneratePackageOnBuild=false` until we're ready to publish. `InternalsVisibleTo("CABber.Tests")` so tests can exercise `Interop` types directly.

`CABber.Tests.csproj`: `net10.0` only, xUnit (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`), `ProjectReference` to `src/CABber`.

**Interop code path**: use classic `DllImport` + `UnmanagedFunctionPointer` delegates uniformly across both TFMs (not the newer source-generated `LibraryImport`) — `LibraryImport` can't marshal the C-callback function pointers FCI/FDI require, and `netstandard2.0` doesn't support it anyway. One shared interop implementation, no `#if` split.

## FCI (create) design

`CCAB`/`ERF` are `[StructLayout(LayoutKind.Sequential)]` structs mirroring `fci.h` (cabinet size limits, folder threshold, `szCab`/`szCabPath` as fixed ANSI byte buffers — cabinet.dll's stable entry points are ANSI; validate/normalize paths to ANSI-safe at the public API boundary and throw `CabinetException` early rather than deep in native code).

FCI callbacks (`FNFCIOPEN/READ/WRITE/CLOSE/SEEK/DELETE/ALLOC/FREE/GETTEMPFILE/FILEPLACED/STATUS/GETNEXTCABINET/GETOPENINFO`) are `__cdecl` delegates passed once into `FCICreate`.

**Two subtleties that are the crux of correctness — get these right first:**

1. **Delegate lifetime**: the delegate instances passed to `FCICreate`/`FDICreate` must stay rooted (strong-referenced as fields) for the entire lifetime of the native handle. If the GC collects them mid-operation, `cabinet.dll` is left holding a dangling function pointer — an intermittent, hard-to-repro crash. `FciContext`/`FdiContext` hold every delegate as a field and are only allowed to go out of scope after `SafeHandle.Dispose()` completes.
2. **File handle table**: FCI/FDI treat the `IntPtr hf` passed to open/read/write/seek/close as an opaque token they never dereference — you don't need a real Win32 `HANDLE`. `FileHandleTable` (instance-scoped per build/extract operation, not static) maps an incrementing token to a real `FileStream`, so `FNFCIOPEN` opens+registers, `FNFCIREAD`/`WRITE` look up + `Marshal.Copy`, `FNFCICLOSE` releases+disposes.

`FciContext : IDisposable` wraps `FciContextSafeHandle` (`SafeHandle` whose `ReleaseHandle` calls `FCIDestroy`), exposes `AddFile(sourcePath, nameInCab)` (calls `FCIAddFile`, throws via `ErrorTranslator` on failure) and `FlushCabinet()`. All `internal` — the public `CabinetBuilder` composes it.

## FDI (extract) design

`FDICreate` takes the same alloc/free/open/read/write/close/seek delegate shapes as FCI (reused). `FDICopy` drives extraction via a single `FNFDINOTIFY` callback receiving notification codes (`CABINET_INFO`, `COPY_FILE`, `CLOSE_FILE_INFO`, `NEXT_CABINET`, ...).

`FdiContext.OnNotify`: on `CopyFile`, read the stored-in-cab relative path from `notification.psz1`, **sanitize it against path traversal** (reject/strip `..` segments — zip-slip-style check) before combining with the destination directory, open+register a `FileStream`, return the token (or `IntPtr.Zero` to skip extraction when only listing). On `CloseFileInfo`, release the handle and apply the DOS date/time + attributes. The same context/pass serves both `ExtractAll` (writes files) and `ListFiles` (metadata only, `CopyFile` returns zero) — one native pass, one code path, a bool flag switches behavior.

## Public API

Both builder and extractor are exposed as interfaces so consumers can mock them in tests (e.g. code that calls into CABber can depend on `ICabinetBuilder`/`ICabinetExtractor` and substitute a fake in unit tests without touching real cabinet files). `CabinetExtractor` additionally needs a factory since its constructor takes the cabinet path directly — `ICabinetExtractorFactory` wraps that so the *creation* of an extractor is also mockable, not just its methods.

```csharp
public interface ICabinetBuilder : IDisposable
{
    ICabinetBuilder AddFile(string sourceFilePath, string? nameInCabinet = null);
    ICabinetBuilder AddDirectory(string sourceDirectoryPath, string? baseNameInCabinet = null, bool recursive = true);
    void Build(string cabinetPath);
}

public interface ICabinetExtractor
{
    IReadOnlyList<CabinetEntry> ListFiles();
    void ExtractAll(string destinationDirectory, bool overwrite = true);
    void ExtractFile(CabinetEntry entry, string destinationDirectory, bool overwrite = true);
}

public interface ICabinetExtractorFactory
{
    ICabinetExtractor Open(string cabinetPath);
}

[SupportedOSPlatform("windows")]
public sealed class CabinetBuilder : ICabinetBuilder
{
    public CabinetBuilder(CabinetBuilderOptions? options = null);
    public ICabinetBuilder AddFile(string sourceFilePath, string? nameInCabinet = null);
    public ICabinetBuilder AddDirectory(string sourceDirectoryPath, string? baseNameInCabinet = null, bool recursive = true);
    public void Build(string cabinetPath);
    public static void Build(string cabinetPath, IEnumerable<string> sourceFiles); // one-shot convenience, not part of the interface (statics aren't mockable anyway)
    public void Dispose();
}

public sealed class CabinetBuilderOptions
{
    public long MaxCabinetSize { get; init; } = int.MaxValue;
    public CompressionType Compression { get; init; } = CompressionType.MsZip;
    public IProgress<CabinetProgress>? Progress { get; init; }
}

public enum CompressionType { None, MsZip, Lzx } // Lzx: interop plumbing only, not exercised by tests in v1

[SupportedOSPlatform("windows")]
public sealed class CabinetExtractor : ICabinetExtractor
{
    public CabinetExtractor(string cabinetPath);
    public IReadOnlyList<CabinetEntry> ListFiles();
    public void ExtractAll(string destinationDirectory, bool overwrite = true);
    public void ExtractFile(CabinetEntry entry, string destinationDirectory, bool overwrite = true);
    public static void ExtractAll(string cabinetPath, string destinationDirectory); // one-shot convenience
}

public sealed class CabinetExtractorFactory : ICabinetExtractorFactory
{
    public ICabinetExtractor Open(string cabinetPath) => new CabinetExtractor(cabinetPath);
}

public sealed record CabinetEntry(string Name, DateTime LastWriteTime, FileAttributes Attributes, long UncompressedSize);
```

Note on fluent chaining: `AddFile`/`AddDirectory` return `ICabinetBuilder` (the interface type) rather than the concrete `CabinetBuilder`, so chaining works identically whether a consumer holds the concrete type or the interface — this is the standard trade-off for interface-based fluent builders (slightly less specific IntelliSense on the concrete type, in exchange for full mockability).

Exceptions: `CabinetException` (base, carries native `ErrorCode`/`ErrorType`) with `CabinetCorruptException`, `CabinetNotFoundException`, `CabinetIOException` subtypes, all produced by `ErrorTranslator` — the single seam every native failure path funnels through.

Layering: `CABber.Interop.*` is entirely `internal` (P/Invoke, structs, delegates, the two `*Context` classes); `CABber.*` root namespace is the public surface (interfaces + concrete implementations) and never leaks `IntPtr`/delegates/structs.

## CI workflow — `.github/workflows/build.yml`

New workflow alongside the existing Claude bot workflows (those stay on `ubuntu-latest`, untouched). Must run on `windows-latest` since `cabinet.dll` only resolves on Windows:

```yaml
name: Build and Test
on:
  push: { branches: [main] }
  pull_request: { branches: [main] }
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet restore CABber.sln
      - run: dotnet build CABber.sln --configuration Release --no-restore
      - run: dotnet test CABber.sln --configuration Release --no-build --logger "trx;LogFileName=test-results.trx" --results-directory TestResults
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: test-results, path: TestResults }
```

## Testing strategy

- **Round-trip**: build a cab from a mix of temp files (empty, small text, large binary >64KB to exercise multi-callback buffered I/O, nested directories via `AddDirectory`), extract it, assert same relative paths + byte-for-byte content + DOS-resolution-tolerant timestamps.
- **ListFiles**: assert metadata matches without writing to disk, and no file handles leak.
- **Error paths**: nonexistent cabinet → `CabinetNotFoundException`; corrupted/truncated cab fixture → `CabinetCorruptException`; nonexistent source file in `AddFile` → fail fast at the public API boundary; a crafted cab with a `..\..\evil.txt` entry name → extraction throws rather than escaping the destination directory.
- **Cross-oracle fixture**: include one `.cab` built via `makecab.exe` (not CABber itself) so FDI extraction is validated against an independent cabinet, decoupling "does extraction work" from "did our own FCI just happen to produce something our own FDI accepts."
- **Delegate-lifetime regression test**: force `GC.Collect()` during a build/extract loop to guard against the delegate-collected-too-early bug class.

## Verification

1. `dotnet restore CABber.sln && dotnet build CABber.sln -c Release` — must succeed for both `net10.0` and `netstandard2.0` TFMs.
2. `dotnet test CABber.sln -c Release` — all round-trip/error-path tests green, run on real Windows so `cabinet.dll` interop is exercised for real (not mocked).
3. Push a branch / open a PR to confirm the `build.yml` workflow runs and passes on `windows-latest`, alongside the existing Claude bot workflows.
