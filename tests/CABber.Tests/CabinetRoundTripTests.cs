using CABber;
using CABber.Tests.TestFixtures;
using Xunit;

namespace CABber.Tests;

public class CabinetRoundTripTests : IDisposable
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
    public void BuildThenExtract_MixedFiles_RoundTripsByteForByte()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var emptyFile = Path.Combine(sourceDir, "empty.txt");
        var smallFile = Path.Combine(sourceDir, "small.txt");
        var nestedDir = Path.Combine(sourceDir, "nested");
        Directory.CreateDirectory(nestedDir);
        var nestedFile = Path.Combine(nestedDir, "child.txt");
        var largeFile = Path.Combine(sourceDir, "large.bin");

        File.WriteAllBytes(emptyFile, Array.Empty<byte>());
        File.WriteAllText(smallFile, "hello cabinet");
        File.WriteAllText(nestedFile, "nested file content");

        var random = new Random(98765);
        var largeBuffer = new byte[200 * 1024];
        random.NextBytes(largeBuffer);
        File.WriteAllBytes(largeFile, largeBuffer);

        var cabPath = GetTempCabinetPath();
        using (var builder = new CabinetBuilder())
        {
            builder.AddDirectory(sourceDir);
            builder.Build(cabPath);
        }

        var destDir = CreateTempDirectory();
        new CabinetExtractor(cabPath).ExtractAll(destDir);

        AssertFileContentEqual(emptyFile, Path.Combine(destDir, "empty.txt"));
        AssertFileContentEqual(smallFile, Path.Combine(destDir, "small.txt"));
        AssertFileContentEqual(nestedFile, Path.Combine(destDir, "nested", "child.txt"));
        AssertFileContentEqual(largeFile, Path.Combine(destDir, "large.bin"));
    }

    [Fact]
    public void BuildThenListFiles_MatchesAddedEntries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var file1 = Path.Combine(sourceDir, "one.txt");
        var file2 = Path.Combine(sourceDir, "two.txt");
        File.WriteAllText(file1, "one");
        File.WriteAllText(file2, "twotwo");

        var cabPath = GetTempCabinetPath();
        using (var builder = new CabinetBuilder())
        {
            builder.AddFile(file1).AddFile(file2);
            builder.Build(cabPath);
        }

        var entries = new CabinetExtractor(cabPath).ListFiles();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "one.txt" && e.UncompressedSize == 3);
        Assert.Contains(entries, e => e.Name == "two.txt" && e.UncompressedSize == 6);
    }

    [Fact]
    public void CrossOracle_MakecabProducedCabinet_ExtractsByteForByte()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourceDir = CreateTempDirectory();
        var sourceFile = Path.Combine(sourceDir, "oracle.txt");
        File.WriteAllText(sourceFile, "content produced independently of CABber, via makecab.exe");

        var cabPath = GetTempCabinetPath();
        if (!CabinetFixtures.TryBuildCrossOracleCabinet(sourceFile, cabPath, out _))
        {
            // makecab.exe isn't guaranteed to be present in every Windows sandbox; skip rather than
            // fail the suite when this specific cross-oracle tool genuinely isn't available.
            return;
        }

        var destDir = CreateTempDirectory();
        new CabinetExtractor(cabPath).ExtractAll(destDir);

        AssertFileContentEqual(sourceFile, Path.Combine(destDir, "oracle.txt"));
    }

    [Fact]
    public void ExtractAll_InLoop_SurvivesForcedGarbageCollectionBetweenNativeCalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Regression test for the delegate-lifetime subtlety in docs/plan.md, mirrored for FDI: the
        // callback delegates passed to FDICreate must stay rooted across every later FDICopy call.
        var sourceDir = CreateTempDirectory();
        var sourceFile = Path.Combine(sourceDir, "gc.txt");
        File.WriteAllText(sourceFile, "guard against delegates collected across separate extracts");

        var cabPath = GetTempCabinetPath();
        using (var builder = new CabinetBuilder())
        {
            builder.AddFile(sourceFile);
            builder.Build(cabPath);
        }

        for (var i = 0; i < 10; i++)
        {
            var destDir = CreateTempDirectory();
            var extractor = new CabinetExtractor(cabPath);

            ForceFullGarbageCollection();
            var entries = extractor.ListFiles();
            ForceFullGarbageCollection();
            extractor.ExtractAll(destDir);
            ForceFullGarbageCollection();

            Assert.Single(entries);
            AssertFileContentEqual(sourceFile, Path.Combine(destDir, "gc.txt"));
        }
    }

    private static void ForceFullGarbageCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }

    private static void AssertFileContentEqual(string expectedPath, string actualPath)
    {
        Assert.True(File.Exists(actualPath), $"Expected extracted file at '{actualPath}'.");
        Assert.Equal(File.ReadAllBytes(expectedPath), File.ReadAllBytes(actualPath));
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
}
