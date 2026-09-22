using Tortoise.Contracts.Mutation;
using Tortoise.Core.Transactions;

namespace Tortoise.Core.FaultInjection;

public static class FaultInjectionGate
{
    public static bool IsEnabled() =>
        TortoiseEnvironmentVariables.IsTruthy(
            Environment.GetEnvironmentVariable(TortoiseEnvironmentVariables.FaultInjection));

    public static void RequireEnabled()
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException(
                "Fault injection is disabled. Set TORTOISE_FAULT_INJECTION=1 to run fault scenarios.");
        }
    }
}

public static class FaultInjectionScenarioCatalog
{
    private static readonly IReadOnlyList<FaultScenarioDefinition> Definitions =
    [
        new(
            FaultInjectionScenario.ProcessCrash,
            FaultInjectionCheckpoint.DuringInstall,
            "Process crash",
            "Simulates the broker or UI terminating mid-install, leaving an interrupted transaction."),
        new(
            FaultInjectionScenario.UnexpectedReboot,
            FaultInjectionCheckpoint.AfterInstallBeforeVerification,
            "Unexpected reboot",
            "Simulates a reboot after install returns but before post-install verification completes."),
        new(
            FaultInjectionScenario.NetworkFailure,
            FaultInjectionCheckpoint.DuringDownload,
            "Network failure",
            "Simulates Windows Update download failure due to network interruption."),
        new(
            FaultInjectionScenario.CandidateDisappeared,
            FaultInjectionCheckpoint.DuringDownload,
            "Candidate disappearance",
            "Simulates the Windows Update candidate vanishing after planning."),
    ];

    public static IReadOnlyList<FaultScenarioDefinition> All => Definitions;

    public static FaultScenarioDefinition Get(FaultInjectionScenario scenario) =>
        Definitions.Single(definition => definition.Scenario == scenario);

    public static bool TryParse(string value, out FaultInjectionScenario scenario)
    {
        if (Enum.TryParse(value, ignoreCase: true, out scenario))
        {
            var parsed = scenario;
            return Definitions.Any(definition => definition.Scenario == parsed);
        }

        scenario = default;
        return false;
    }
}

public static class FaultInjectionJournal
{
    public const string Prefix = "[fault:";

    public static string FormatMessage(FaultInjectionScenario scenario, string message) =>
        $"{Prefix}{scenario}] {message}";

    public static FaultInjectionScenario? TryParseScenario(IReadOnlyList<OperationJournalEntry> journal)
    {
        foreach (var entry in journal.Reverse())
        {
            if (entry.Message is null || !entry.Message.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var endIndex = entry.Message.IndexOf(']', Prefix.Length - 1);
            if (endIndex <= Prefix.Length)
            {
                continue;
            }

            var scenarioText = entry.Message[Prefix.Length..endIndex];
            if (Enum.TryParse<FaultInjectionScenario>(scenarioText, out var scenario))
            {
                return scenario;
            }
        }

        return null;
    }
}
