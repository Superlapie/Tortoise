using System.Text.Json;
using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Guest;

public sealed record VmHarnessGuestTarget(string Name, string HyperVVmId);

public sealed record VmHarnessRemoteCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    int DurationMs);

public interface IVmHarnessGuestTransport
{
    bool IsConfigured { get; }

    Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        VmHarnessGuestTarget target,
        CancellationToken cancellationToken = default);

    Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        VmHarnessGuestTarget target,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default);

    Task<bool> ProbeReachabilityAsync(
        VmHarnessGuestTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record VmHarnessGuestCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public static VmHarnessGuestCommandResult FromRemote(VmHarnessRemoteCommandResult remote) =>
        new(
            remote.ExitCode,
            remote.StandardOutput,
            remote.StandardError,
            TimeSpan.FromMilliseconds(remote.DurationMs));
}

public static class VmHarnessGuestTargetFactory
{
    public static VmHarnessGuestTarget FromVmTarget(VmHarnessVmTarget target) =>
        new(target.Name, target.HyperVVmId);
}

public static class VmHarnessLabEnvironment
{
    public static Dictionary<string, string> MutationCommandEnvironment =>
        new()
        {
            ["TORTOISE_MUTATION_TESTS"] = "1",
            ["TORTOISE_ALLOW_VM_INSTALL"] = "1",
        };
}

public static class VmHarnessGuestStatusParser
{
    public static VmHarnessGuestEnvironmentProof FromLabStatusJson(HarnessLabStatusJson status, string rawJson)
    {
        return new VmHarnessGuestEnvironmentProof(
            status.IsDisposableVm,
            status.MutationTestsEnabled,
            status.VmInstallExplicitlyAllowed,
            status.MutationCapabilityEnabled,
            status.MutationEnvironment,
            status.Reason,
            rawJson);
    }
}

public static class VmHarnessGuestEvidenceCommands
{
    public static string Recommend(string dbArg) => $"tortoise recommend {dbArg}";
    public static string PlanJson(string dbArg) => $"tortoise plan --json {dbArg}";
    public static string LabPreflightJson(Guid planId, string dbArg) =>
        $"tortoise-lab vm preflight {planId} --json {dbArg}";
    public static string LabEvidenceSnapshot(string dbArg) => $"tortoise-lab vm evidence snapshot --json {dbArg}";
    public static string LabInstallJson(Guid planId, string dbArg) => $"tortoise-lab vm install {planId} --json {dbArg}";
}

public sealed record VmHarnessPlanListJson(IReadOnlyList<VmHarnessPlanSummaryJson> Plans);

public sealed record VmHarnessPlanSummaryJson(Guid PlanId, string RiskLevel, string Classification);

public static class VmHarnessPlanJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<VmHarnessPlanSummaryJson> ParsePlans(string json)
    {
        var payload = JsonSerializer.Deserialize<VmHarnessPlanListJson>(json, JsonOptions);
        return payload?.Plans ?? [];
    }

    public static Guid? SelectEligibleBaselinePlan(string json)
    {
        var eligible = ParsePlans(json)
            .Where(plan =>
                string.Equals(plan.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan.Classification, "WindowsRecommended", StringComparison.OrdinalIgnoreCase))
            .Select(plan => plan.PlanId)
            .FirstOrDefault();

        return eligible == Guid.Empty ? null : eligible;
    }
}
