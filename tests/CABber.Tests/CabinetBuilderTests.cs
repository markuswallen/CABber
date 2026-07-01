using CABber;
using CABber.Interop;
using Xunit;

namespace CABber.Tests;

public class CabinetBuilderTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup; leaked temp files/dirs don't fail the test run.
            }
        }
    }

    [Fact]
    public void Build_FromFiles_ProducesValidCabinet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var emptyFile = Path.Combine(sourceDir, "empty.txt");
        var smallFile = Path.Combine(sourceDir, "small.txt");
        File.WriteAllBytes(emptyFile, Array.Empty<byte>());
        File.WriteAllText(smallFile, "hello cabinet");

        var cabPath = GetTempCabinetPath();

        using (var builder = new CabinetBuilder())
        {
            builder.AddFile(emptyFile).AddFile(smallFile, "renamed.txt");
            builder.Build(cabPath);
        }

        AssertIsValidCabinet(cabPath);
    }

    [Fact]
    public void Build_FromDirectory_IncludesNestedFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var nestedDir = Path.Combine(sourceDir, "nested");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "root file");
        File.WriteAllText(Path.Combine(nestedDir, "child.txt"), "nested file");

        var cabPath = GetTempCabinetPath();

        using (var builder = new CabinetBuilder())
        {
            builder.AddDirectory(sourceDir);
            builder.Build(cabPath);
        }

        AssertIsValidCabinet(cabPath);
    }

    [Fact]
    public void Build_WithLargeBinaryFile_ExercisesMultiCallbackIo()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var largeFile = Path.Combine(sourceDir, "large.bin");
        var random = new Random(12345);
        var buffer = new byte[128 * 1024];
        random.NextBytes(buffer);
        File.WriteAllBytes(largeFile, buffer);

        var cabPath = GetTempCabinetPath();

        using (var builder = new CabinetBuilder())
        {
            builder.AddFile(largeFile);
            builder.Build(cabPath);
        }

        AssertIsValidCabinet(cabPath);
    }

    [Fact]
    public void Build_ReportsProgressPerFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var file1 = Path.Combine(sourceDir, "a.txt");
        var file2 = Path.Combine(sourceDir, "b.txt");
        File.WriteAllText(file1, "aaaa");
        File.WriteAllText(file2, "bbbb");

        var progress = new SynchronousProgress();
        var cabPath = GetTempCabinetPath();

        using (var builder = new CabinetBuilder(new CabinetBuilderOptions { Progress = progress }))
        {
            builder.AddFile(file1).AddFile(file2);
            builder.Build(cabPath);
        }

        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal(1, progress.Reports[0].FilesProcessed);
        Assert.Equal(2, progress.Reports[1].FilesProcessed);
        Assert.Equal(2, progress.Reports[1].TotalFiles);
    }

    [Fact]
    public void AddFile_NonexistentSourceFile_ThrowsImmediately()
    {
        using var builder = new CabinetBuilder();
        var missingPath = Path.Combine(Path.GetTempPath(), "CABberTests_missing_" + Guid.NewGuid().ToString("N") + ".txt");

        Assert.Throws<FileNotFoundException>(() => builder.AddFile(missingPath));
    }

    [Fact]
    public void AddDirectory_NonexistentSourceDirectory_ThrowsImmediately()
    {
        using var builder = new CabinetBuilder();
        var missingDir = Path.Combine(Path.GetTempPath(), "CABberTests_missing_dir_" + Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => builder.AddDirectory(missingDir));
    }

    [Fact]
    public void AddDirectory_EmptyDirectory_ReturnsSameBuilderWithoutThrowing()
    {
        using var builder = new CabinetBuilder();
        var sourceDir = CreateTempDirectory();

        var result = builder.AddDirectory(sourceDir);

        Assert.Same(builder, result);
    }

    [Fact]
    public void Build_CalledTwice_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var file = Path.Combine(sourceDir, "file.txt");
        File.WriteAllText(file, "content");

        var cabPath = GetTempCabinetPath();

        using var builder = new CabinetBuilder();
        builder.AddFile(file);
        builder.Build(cabPath);

        Assert.Throws<InvalidOperationException>(() => builder.Build(GetTempCabinetPath()));
    }

    [Fact]
    public void FciContext_SurvivesForcedGarbageCollectionBetweenNativeCalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Regression test for the delegate-lifetime subtlety in docs/plan.md: the callback
        // delegates passed to FCICreate must stay rooted across every later FCIAddFile/
        // FCIFlushCabinet call, not just for the duration of FCICreate itself. Forcing a
        // blocking, compacting GC between each native call would surface a dangling native
        // function pointer if FciContext didn't root its delegates as instance fields.
        var sourceDir = CreateTempDirectory();
        var file1 = Path.Combine(sourceDir, "one.txt");
        var file2 = Path.Combine(sourceDir, "two.txt");
        File.WriteAllText(file1, "first file content");
        File.WriteAllText(file2, "second file content");

        var cabPath = GetTempCabinetPath();
        var fullCabPath = Path.GetFullPath(cabPath);

        var ccab = new CCAB
        {
            cb = int.MaxValue,
            cbFolderThresh = 0,
            iCab = 1,
            iDisk = 0,
            fFailOnIncompressible = 0,
            setID = 0,
            szDisk = string.Empty,
            szCab = Path.GetFileName(fullCabPath),
            szCabPath = Path.GetDirectoryName(fullCabPath) + Path.DirectorySeparatorChar,
        };

        using (var context = new FciContext(ccab, CompressionType.MsZip, progress: null, totalFiles: 2, totalBytes: 40))
        {
            ForceFullGarbageCollection();

            context.AddFile(file1, "one.txt");
            ForceFullGarbageCollection();

            context.AddFile(file2, "two.txt");
            ForceFullGarbageCollection();

            context.FlushCabinet();
        }

        AssertIsValidCabinet(cabPath);
    }

    [Fact]
    public void Build_InLoop_SurvivesForcedGarbageCollectionBetweenBuilds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var file = Path.Combine(sourceDir, "gc-regression.txt");
        File.WriteAllText(file, "guard against delegates collected across separate builds");

        for (var i = 0; i < 10; i++)
        {
            var cabPath = GetTempCabinetPath();

            using (var builder = new CabinetBuilder())
            {
                builder.AddFile(file);
                ForceFullGarbageCollection();
                builder.Build(cabPath);
            }

            AssertIsValidCabinet(cabPath);
        }
    }

    private static void ForceFullGarbageCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }

    private static void AssertIsValidCabinet(string path)
    {
        Assert.True(File.Exists(path));

        using var stream = File.OpenRead(path);
        Assert.True(stream.Length > 4);

        var signature = new byte[4];
        var read = stream.Read(signature, 0, 4);
        Assert.Equal(4, read);
        Assert.Equal(new byte[] { (byte)'M', (byte)'S', (byte)'C', (byte)'F' }, signature);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CABberTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    private string GetTempCabinetPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "CABberTests_" + Guid.NewGuid().ToString("N") + ".cab");
        _tempPaths.Add(path);
        return path;
    }

    private sealed class SynchronousProgress : IProgress<CabinetProgress>
    {
        public List<CabinetProgress> Reports { get; } = new();

        public void Report(CabinetProgress value) => Reports.Add(value);
    }
}
