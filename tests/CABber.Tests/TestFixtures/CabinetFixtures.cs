using System.Diagnostics;
using CABber;

namespace CABber.Tests.TestFixtures;

/// <summary>
/// Builds cabinet fixtures at test-run time instead of shipping committed binary .cab files.
/// The "valid" / "corrupted" / "path-traversal" fixtures are all produced by CABber's own
/// <see cref="CabinetBuilder"/> (deterministic, always in sync with the current code), while the
/// cross-oracle fixture is produced by shelling out to the real <c>makecab.exe</c> so it is validated
/// against an independently-produced cabinet rather than a stale binary checked into source control.
/// </summary>
internal static class CabinetFixtures
{
    public static void BuildValidCabinet(string cabPath, IReadOnlyDictionary<string, string> filesByNameInCabinet, string workDir)
    {
        using var builder = new CabinetBuilder();

        foreach (var (nameInCabinet, content) in filesByNameInCabinet)
        {
            var sourcePath = Path.Combine(workDir, Guid.NewGuid().ToString("N") + ".src");
            File.WriteAllText(sourcePath, content);
            builder.AddFile(sourcePath, nameInCabinet);
        }

        builder.Build(cabPath);
    }

    /// <summary>Builds a structurally valid cabinet, then flips bytes past the header to corrupt its folder/file/data records.</summary>
    public static void BuildCorruptedCabinet(string cabPath, string workDir)
    {
        var sourcePath = Path.Combine(workDir, "corrupt-source.txt");
        File.WriteAllText(sourcePath, "content that will be corrupted after the cabinet is built");

        using (var builder = new CabinetBuilder())
        {
            builder.AddFile(sourcePath, "corrupt-source.txt");
            builder.Build(cabPath);
        }

        using var stream = new FileStream(cabPath, FileMode.Open, FileAccess.ReadWrite);
        var start = stream.Length / 2;
        var corruption = new byte[Math.Min(32, stream.Length - start)];
        Array.Fill(corruption, (byte)0xFF);
        stream.Seek(start, SeekOrigin.Begin);
        stream.Write(corruption, 0, corruption.Length);
    }

    /// <summary>
    /// Builds a cabinet with an entry name crafted to escape the destination directory on extraction
    /// (a zip-slip-style attack). <see cref="CabinetBuilder"/> does not reject ".." in the
    /// name-in-cabinet parameter (only the extractor is responsible for rejecting it), so this can be
    /// produced with CABber's own builder.
    /// </summary>
    public static void BuildPathTraversalCabinet(string cabPath, string workDir)
    {
        var sourcePath = Path.Combine(workDir, "evil.txt");
        File.WriteAllText(sourcePath, "should never escape the destination directory");

        using var builder = new CabinetBuilder();
        builder.AddFile(sourcePath, @"..\..\evil.txt");
        builder.Build(cabPath);
    }

    /// <summary>Attempts to build a cabinet via the real makecab.exe. Returns false (with a reason) if it isn't available.</summary>
    public static bool TryBuildCrossOracleCabinet(string sourceFilePath, string cabPath, out string? skipReason)
    {
        if (!OperatingSystem.IsWindows())
        {
            skipReason = "not running on Windows";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo("makecab.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(sourceFilePath);
            psi.ArgumentList.Add(cabPath);

            using var process = Process.Start(psi);
            if (process is null)
            {
                skipReason = "could not start makecab.exe";
                return false;
            }

            if (!process.WaitForExit(30_000))
            {
                skipReason = "makecab.exe timed out";
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(cabPath))
            {
                skipReason = $"makecab.exe exited with code {process.ExitCode}";
                return false;
            }

            skipReason = null;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            skipReason = $"makecab.exe unavailable: {ex.Message}";
            return false;
        }
    }
}
