using System.Text;
using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Reporting;

public static class VmHarnessReportWriter
{
    public static string BuildReport(
        VmHarnessRun run,
        VmHarnessVerdict verdict,
        VmHarnessMutationOutcome mutationOutcome,
        VmHarnessRestoreOutcome restoreOutcome,
        VmHarnessScenarioResult? scenarioResult,
        VmHarnessRestoreResult? restoreResult,
        VmHarnessDualVmProofResult? dualProof,
        VmHarnessSafetyGateResult? safetyGate)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Tortoise VM Harness Report");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{run.RunId}`");
        builder.AppendLine($"Scenario: `{run.ScenarioId}`");
        builder.AppendLine($"Mode: `{run.Mode}`");
        builder.AppendLine($"VM: `{run.VmName}`");
        if (!string.IsNullOrWhiteSpace(run.HyperVVmId))
        {
            builder.AppendLine($"Hyper-V VM ID: `{run.HyperVVmId}`");
        }

        if (!string.IsNullOrWhiteSpace(run.CheckpointName))
        {
            builder.AppendLine($"Checkpoint: `{run.CheckpointName}`");
        }

        builder.AppendLine($"Harness version: `{run.HarnessVersion}`");
        builder.AppendLine($"Tortoise commit: `{run.TortoiseCommitSha ?? "unknown"}`");
        builder.AppendLine($"Started (UTC): `{run.StartedAtUtc:o}`");
        builder.AppendLine();
        builder.AppendLine("## Verdict");
        builder.AppendLine();
        builder.AppendLine($"**{verdict}**");
        builder.AppendLine();
        builder.AppendLine("## Tortoise mutation outcome");
        builder.AppendLine();
        builder.AppendLine($"- Outcome: `{mutationOutcome}`");
        if (scenarioResult is not null)
        {
            builder.AppendLine($"- Summary: {scenarioResult.Summary}");
            if (!string.IsNullOrWhiteSpace(scenarioResult.PlanId))
            {
                builder.AppendLine($"- Plan ID: `{scenarioResult.PlanId}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## VM restoration outcome");
        builder.AppendLine();
        builder.AppendLine($"- Outcome: `{restoreOutcome}`");
        if (restoreResult is not null)
        {
            builder.AppendLine($"- Summary: {restoreResult.Summary}");
            if (!string.IsNullOrWhiteSpace(restoreResult.Error))
            {
                builder.AppendLine($"- Error: {restoreResult.Error}");
            }
        }

        if (dualProof is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Host/guest VM proof");
            builder.AppendLine();
            builder.AppendLine($"- Host proof valid: `{dualProof.HostProofValid}`");
            builder.AppendLine($"- Guest proof valid: `{dualProof.GuestProofValid}`");
            builder.AppendLine($"- Proofs agree: `{dualProof.ProofsAgree}`");
            if (!string.IsNullOrWhiteSpace(dualProof.BlockReason))
            {
                builder.AppendLine($"- Block reason: {dualProof.BlockReason}");
            }
        }

        if (safetyGate is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Safety gate");
            builder.AppendLine();
            builder.AppendLine($"- Allowed: `{safetyGate.Allowed}`");
            if (safetyGate.FailedConditions.Count > 0)
            {
                builder.AppendLine("- Failed conditions:");
                foreach (var condition in safetyGate.FailedConditions)
                {
                    builder.AppendLine($"  - {condition}");
                }
            }

            if (safetyGate.UnknownConditions.Count > 0)
            {
                builder.AppendLine("- Unknown conditions:");
                foreach (var condition in safetyGate.UnknownConditions)
                {
                    builder.AppendLine($"  - {condition}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("> Physical-machine mutation is prohibited. This harness is for disposable, checkpointed virtual machines only.");
        return builder.ToString();
    }
}
