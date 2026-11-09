# CABber

A .NET wrapper around the Windows cabinet (`.cab`) creation and extraction
APIs.

> **Windows only.** CABber calls the native FCI/FDI APIs in `cabinet.dll`. The
> package targets `netstandard2.0` for broad compatibility, but that does **not**
> make it cross-platform: on Linux or macOS the calls fail at runtime.

## Features

- Create cabinets from individual files or whole directories.
- List and extract the contents of existing cabinets.
- Choose the compression type: `None`, `MsZip`, or `Lzx`.
- Split output into multiple cabinets with a maximum cabinet size.
- Progress reporting through `IProgress<CabinetProgress>`.
- Interface-based API (`ICabinetBuilder`, `ICabinetExtractor`,
  `ICabinetExtractorFactory`) that is easy to mock in tests.

## Installation

CABber is distributed as a NuGet package:

```
dotnet add package CABber
```

While CABber is in prerelease, add `--prerelease`.

## Requirements

- **Windows** (any version that ships `cabinet.dll`). CABber wraps the native
  FCI (File Compression Interface) and FDI (File Decompression Interface) APIs,
  which only exist on Windows.
- A project targeting `net10.0` or any runtime compatible with
  `netstandard2.0` (.NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+).
- `CabinetBuilder` and `CabinetExtractor` are annotated with
  `[SupportedOSPlatform("windows")]`, so the platform compatibility analyzer
  warns when you use them from code that may run on other platforms. Guard
  such code with `OperatingSystem.IsWindows()`.

## Building a cabinet

```csharp
using CABber;

using ICabinetBuilder builder = new CabinetBuilder();

builder
    .AddFile(@"C:\src\readme.txt")
    .AddDirectory(@"C:\src\assets", baseNameInCabinet: "assets")
    .Build(@"C:\out\package.cab");
```

## Extracting a cabinet

```csharp
using CABber;

ICabinetExtractorFactory factory = new CabinetExtractorFactory();
ICabinetExtractor extractor = factory.Open(@"C:\out\package.cab");

foreach (CabinetEntry entry in extractor.ListFiles())
{
    Console.WriteLine($"{entry.Name} ({entry.UncompressedSize} bytes)");
}

extractor.ExtractAll(@"C:\out\extracted");
```

## Options

Pass a `CabinetBuilderOptions` instance to `CabinetBuilder` to control how the
cabinet is built:

```csharp
var options = new CabinetBuilderOptions
{
    Compression = CompressionType.Lzx,
    MaxCabinetSize = 50 * 1024 * 1024, // 50 MB
    Progress = new Progress<CabinetProgress>(p => Console.WriteLine(p)),
};

using ICabinetBuilder builder = new CabinetBuilder(options);
```

- `Compression` — the compression algorithm to use: `None`, `MsZip`, or
  `Lzx` (default).
- `MaxCabinetSize` — the maximum size, in bytes, of the cabinet being built.
- `Progress` — an optional `IProgress<CabinetProgress>` for reporting build
  progress.

## License

Licensed under the [MIT License](LICENSE).
