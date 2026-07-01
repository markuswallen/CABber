using CABber;
using CABber.Tests.TestFixtures;
using Xunit;

namespace CABber.Tests;

public class CabinetExtractorTests : IDisposable
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
    public void ListFiles_ReturnsMetadataWithoutWritingToDisk()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildValidCabinet(cabPath, new Dictionary<string, string>
        {
            ["a.txt"] = "aaaa",
            ["nested\\b.txt"] = "bbbbb",
        }, workDir);

        var extractor = new CabinetExtractor(cabPath);
        var entries = extractor.ListFiles();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "a.txt" && e.UncompressedSize == 4);
        Assert.Contains(entries, e => e.Name == "nested\\b.txt" && e.UncompressedSize == 5);

        var wouldBeDestination = Path.Combine(workDir, "extracted-a.txt");
        Assert.False(File.Exists(wouldBeDestination));
    }

    [Fact]
    public void ExtractFile_SingleEntry_WritesOnlyThatFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildValidCabinet(cabPath, new Dictionary<string, string>
        {
            ["a.txt"] = "aaaa",
            ["nested\\b.txt"] = "bbbbb",
        }, workDir);

        var extractor = new CabinetExtractor(cabPath);
        var target = extractor.ListFiles().Single(e => e.Name == "a.txt");
        var destDir = CreateTempDirectory();

        extractor.ExtractFile(target, destDir);

        Assert.True(File.Exists(Path.Combine(destDir, "a.txt")));
        Assert.False(File.Exists(Path.Combine(destDir, "nested", "b.txt")));
        Assert.False(Directory.Exists(Path.Combine(destDir, "nested")));
    }

    [Fact]
    public void ExtractFile_EntryNotInCabinet_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildValidCabinet(cabPath, new Dictionary<string, string> { ["a.txt"] = "aaaa" }, workDir);

        var extractor = new CabinetExtractor(cabPath);
        var bogusEntry = new CabinetEntry("does-not-exist.txt", DateTime.Now, FileAttributes.Normal, 0);

        Assert.Throws<CabinetException>(() => extractor.ExtractFile(bogusEntry, CreateTempDirectory()));
    }

    [Fact]
    public void ListFiles_NonexistentCabinet_ThrowsCabinetNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "CABberTests_missing_" + Guid.NewGuid().ToString("N") + ".cab");
        var extractor = new CabinetExtractor(missingPath);

        Assert.Throws<CabinetNotFoundException>(() => extractor.ListFiles());
    }

    [Fact]
    public void ExtractAll_NonexistentCabinet_ThrowsCabinetNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "CABberTests_missing_" + Guid.NewGuid().ToString("N") + ".cab");
        var extractor = new CabinetExtractor(missingPath);

        Assert.Throws<CabinetNotFoundException>(() => extractor.ExtractAll(CreateTempDirectory()));
    }

    [Fact]
    public void ListFiles_CorruptedCabinet_ThrowsCabinetCorruptException()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildCorruptedCabinet(cabPath, workDir);

        var extractor = new CabinetExtractor(cabPath);

        Assert.Throws<CabinetCorruptException>(() => extractor.ListFiles());
    }

    [Fact]
    public void ExtractAll_PathTraversalEntry_ThrowsRatherThanEscapingDestination()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildPathTraversalCabinet(cabPath, workDir);

        var destDir = CreateTempDirectory();
        var extractor = new CabinetExtractor(cabPath);

        Assert.Throws<CabinetException>(() => extractor.ExtractAll(destDir));

        var escapedPath = Path.GetFullPath(Path.Combine(destDir, "..", "..", "evil.txt"));
        Assert.False(File.Exists(escapedPath));
    }

    [Fact]
    public void ExtractAll_ExistingFileWithoutOverwrite_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildValidCabinet(cabPath, new Dictionary<string, string> { ["a.txt"] = "aaaa" }, workDir);

        var destDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "pre-existing content");

        var extractor = new CabinetExtractor(cabPath);

        Assert.Throws<CabinetException>(() => extractor.ExtractAll(destDir, overwrite: false));
    }

    [Fact]
    public void ExtractAll_ExistingFileWithOverwrite_ReplacesContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workDir = CreateTempDirectory();
        var cabPath = GetTempCabinetPath();
        CabinetFixtures.BuildValidCabinet(cabPath, new Dictionary<string, string> { ["a.txt"] = "new content" }, workDir);

        var destDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "stale content");

        new CabinetExtractor(cabPath).ExtractAll(destDir, overwrite: true);

        Assert.Equal("new content", File.ReadAllText(Path.Combine(destDir, "a.txt")));
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
