using System.Text.Json;
using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Guest;

public sealed record VmHarnessRemoteCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    int DurationMs);

public interface IVmHarnessGuestTransport
{
    bool IsConfigured { get; }

    Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        string vmName,
        CancellationToken cancellationToken = default);

    Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default);

    Task<bool> ProbeReachabilityAsync(
        string vmName,
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
    public static string Preflight(Guid planId, string dbArg) => $"tortoise preflight {planId} {dbArg}";
    public static string LabInstallJson(Guid planId, string dbArg) => $"tortoise-lab vm install {planId} --json {dbArg}";
    public static string ExportReport(string guestPath, string dbArg) => $"tortoise export-report \"{guestPath}\" {dbArg}";
    public static string RecoverList(string dbArg) => $"tortoise recover list {dbArg}";
}

public sealed record VmHarnessPlanListJson(IReadOnlyList<VmHarnessPlanSummaryJson> Plans);

public sealed record VmHarnessPlanSummaryJson(Guid PlanId, string RiskLevel, string Classification);

public static class VmHarnessPlanJsonParser
{
    public static IReadOnlyList<Guid> ParsePlanIds(string json)
    {
        var payload = JsonSerializer.Deserialize<VmHarnessPlanListJson>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return payload?.Plans.Select(plan => plan.PlanId).Distinct().ToList() ?? [];
    }
}
