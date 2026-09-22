using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tortoise.Core.Errors;

namespace Tortoise.Windows.Drivers;

internal static class PnPUtilProcessRunner
{
    private const int DefaultTimeoutMs = 120_000;
    private const int MaxOutputCharacters = 10_000_000;

    internal static string RunEnumDrivers(bool preferStructuredOutput, out bool usedStructuredOutput)
    {
        usedStructuredOutput = false;
        var executablePath = Path.Combine(Environment.SystemDirectory, "pnputil.exe");

        if (preferStructuredOutput)
        {
            try
            {
                var structuredOutput = Execute(
                    executablePath,
                    ["/enum-drivers", "/format", "csv"],
                    DefaultTimeoutMs);
                usedStructuredOutput = true;
                return structuredOutput;
            }
            catch (TortoiseException ex) when (ex.DiagnosticMessage.Contains("exit code", StringComparison.Ordinal))
            {
                // Fall back to legacy output when structured mode is unavailable.
            }
        }

        return Execute(executablePath, ["/enum-drivers"], DefaultTimeoutMs);
    }

    private static string Execute(string executablePath, IReadOnlyList<string> arguments, int timeoutMs)
    {
        if (!File.Exists(executablePath))
        {
            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Unable to enumerate driver packages.",
                $"PnPUtil was not found at '{executablePath}'.");
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding = Encoding.Unicode,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Unable to enumerate driver packages.",
                "Failed to start pnputil.exe.");
        }

        var stdout = ReadLimited(process.StandardOutput, MaxOutputCharacters);
        var stderr = ReadLimited(process.StandardError, MaxOutputCharacters);

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Driver package enumeration timed out.",
                "pnputil.exe exceeded the configured timeout.");
        }

        if (process.ExitCode != 0)
        {
            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Unable to enumerate driver packages.",
                $"pnputil.exe exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: {stderr}");
        }

        return stdout;
    }

    private static string ReadLimited(StreamReader reader, int maxCharacters)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (builder.Length < maxCharacters)
        {
            var read = reader.Read(buffer, 0, Math.Min(buffer.Length, maxCharacters - builder.Length));
            if (read <= 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}
