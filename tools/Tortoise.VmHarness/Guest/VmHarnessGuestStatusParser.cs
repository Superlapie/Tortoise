using System.Text.RegularExpressions;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;

namespace Tortoise.VmHarness.Guest;

public static class VmHarnessGuestStatusParser
{
    private static readonly Regex DisposableVmPattern = new(
        @"Disposable VM detected:\s*(?<value>true|false)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MutationEnabledPattern = new(
        @"Mutation enabled:\s*(?<value>true|false)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EnvironmentPattern = new(
        @"Environment:\s*(?<value>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReasonPattern = new(
        @"Reason:\s*(?<value>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static VmHarnessGuestEnvironmentProof ParseStatus(string raw)
    {
        static bool ParseBool(Match match) =>
            match.Success && bool.TryParse(match.Groups["value"].Value.Trim(), out var value) && value;

        var disposable = ParseBool(DisposableVmPattern.Match(raw));
        var mutationEnabled = ParseBool(MutationEnabledPattern.Match(raw));
        var environment = EnvironmentPattern.Match(raw).Groups["value"].Value.Trim();
        var reason = ReasonPattern.Match(raw).Groups["value"].Value.Trim();

        return new VmHarnessGuestEnvironmentProof(
            disposable,
            MutationTestsEnabled: raw.Contains("TORTOISE_MUTATION_TESTS", StringComparison.OrdinalIgnoreCase),
            AllowVmInstallEnabled: raw.Contains("ALLOW_VM_INSTALL", StringComparison.OrdinalIgnoreCase),
            mutationEnabled,
            string.IsNullOrWhiteSpace(environment) ? "Unknown" : environment,
            string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason,
            VmHarnessRedactor.Redact(raw));
    }
}
