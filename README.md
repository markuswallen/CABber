# CABber

A .NET wrapper around the Windows cab creator stuff

## Installation

CABber is distributed as a NuGet package:

```
dotnet add package CABber
```

## Requirements

CABber wraps the native FCI (File Compression Interface) and FDI (File
Decompression Interface) APIs exported by Windows' `cabinet.dll`. Those APIs
only exist on Windows, so **CABber only runs on Windows**, regardless of
which target framework your project uses.

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
