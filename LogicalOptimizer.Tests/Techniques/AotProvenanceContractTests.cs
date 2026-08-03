using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Semantic contract tests for the F-14 AOT-provenance rules in
///     <c>tools/AotProvenanceCheck.ps1</c> — the function the release evidence bundle uses to
///     refuse an AOT report whose provenance does not match the packaged bytes. Each test runs
///     the REAL PowerShell function (no reimplementation, no network) over a synthetic report:
///     local pre-publish mode must carry a consolidatedPackageSha256 that matches
///     SHA256SUMS.txt, and published-nuget.org mode must carry none (nuget.org
///     repository-signs packages, so its bytes legitimately differ).
/// </summary>
public class AotProvenanceContractTests
{
    private const string Version = "9.9.9";
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void LocalMode_ShaMatchingChecksums_Passes()
    {
        var (exitCode, _) = RunCheck(
            LocalReport(Sha),
            $"{Sha}  LogicalOptimizer.{Version}.nupkg\n");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void LocalMode_ShaMismatch_FailsNamingBothHashes()
    {
        var otherSha = new string('b', 64);
        var (exitCode, output) = RunCheck(
            LocalReport(Sha),
            $"{otherSha}  LogicalOptimizer.{Version}.nupkg\n");

        Assert.NotEqual(0, exitCode);
        Assert.Contains(Sha, output);
        Assert.Contains(otherSha, output);
    }

    [Fact]
    public void LocalMode_MissingSha_Fails()
    {
        var (exitCode, output) = RunCheck(
            LocalReport(sha: null),
            $"{Sha}  LogicalOptimizer.{Version}.nupkg\n");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("consolidatedPackageSha256", output);
    }

    [Fact]
    public void LocalMode_MissingChecksumsFile_Fails()
    {
        var (exitCode, output) = RunCheck(LocalReport(Sha), checksums: null);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("SHA256SUMS", output);
    }

    [Fact]
    public void PublishedMode_NullSha_Passes()
    {
        var (exitCode, _) = RunCheck(PublishedReport(sha: null), checksums: null);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void PublishedMode_UnexpectedLocalSha_Fails()
    {
        var (exitCode, output) = RunCheck(PublishedReport(Sha), checksums: null);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("inconsistent", output);
    }

    private static string LocalReport(string? sha)
    {
        var shaJson = sha is null ? "null" : $"\"{sha}\"";
        return "{ \"source\": \"local packed artifacts (pre-publish): D:\\\\somewhere\\\\artifacts\", " +
               $"\"consolidatedPackageSha256\": {shaJson} }}";
    }

    private static string PublishedReport(string? sha)
    {
        var shaJson = sha is null ? "null" : $"\"{sha}\"";
        return "{ \"source\": \"published nuget.org package\", " +
               $"\"consolidatedPackageSha256\": {shaJson} }}";
    }

    /// <summary>Runs Test-AotProvenance over the given synthetic inputs; exit 0 = consistent.</summary>
    private static (int ExitCode, string Output) RunCheck(string reportJson, string? checksums)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "aot-prov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        try
        {
            var reportPath = Path.Combine(workDir, "aot-report.json");
            File.WriteAllText(reportPath, reportJson);

            var checksumsPath = "";
            if (checksums is not null)
            {
                checksumsPath = Path.Combine(workDir, "SHA256SUMS.txt");
                File.WriteAllText(checksumsPath, checksums);
            }

            var checkScript = Path.Combine(RepositoryRoot(), "tools", "AotProvenanceCheck.ps1");
            var driverPath = Path.Combine(workDir, "driver.ps1");
            File.WriteAllText(driverPath, $$"""
                . '{{checkScript}}'
                $r = Test-AotProvenance -AotReportPath '{{reportPath}}' -ChecksumsPath '{{checksumsPath}}' -Version '{{Version}}'
                if ($null -eq $r) { exit 0 } else { Write-Output $r; exit 1 }
                """);

            var startInfo = new ProcessStartInfo
            {
                FileName = ShellExecutable(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", driverPath })
                startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), "PowerShell provenance check timed out");
            return (process.ExitCode, output);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>pwsh everywhere it exists (Linux/macOS CI); Windows PowerShell as the fallback.</summary>
    private static string ShellExecutable()
    {
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        var pwshName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pwsh.exe" : "pwsh";
        if (pathDirs.Any(d => !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d, pwshName))))
            return "pwsh";
        Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Neither pwsh nor Windows PowerShell is available to run the provenance check");
        return "powershell";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root (LogicalOptimizer.sln)");
    }
}
