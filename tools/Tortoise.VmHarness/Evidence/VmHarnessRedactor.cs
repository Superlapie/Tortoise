using System.Text.RegularExpressions;

namespace Tortoise.VmHarness.Evidence;

public static partial class VmHarnessRedactor
{
    [GeneratedRegex(@"(?i)(password|secret|token|capability|credential)\s*[:=]\s*\S+", RegexOptions.Compiled)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"(?i)--capability=\S+", RegexOptions.Compiled)]
    private static partial Regex CapabilityArgPattern();

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var redacted = SecretPattern().Replace(input, "$1=[REDACTED]");
        redacted = CapabilityArgPattern().Replace(redacted, "--capability=[REDACTED]");
        return redacted;
    }
}

public static class VmHarnessCheckpointNaming
{
    public static string CreateCheckpointName(string runId, DateTimeOffset timestampUtc)
    {
        var shortRun = runId.Length >= 8 ? runId[..8] : runId;
        return $"TortoiseHarness-{timestampUtc:yyyyMMddHHmmssfff}Z-{shortRun}";
    }
}

public static class VmHarnessGitMetadata
{
    public static string? TryResolveCommitSha()
    {
        var envSha = Environment.GetEnvironmentVariable("TORTOISE_COMMIT_SHA")
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(envSha))
        {
            return envSha.Trim();
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
