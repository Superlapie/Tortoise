using System.Text.Json;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Scenarios;

namespace Tortoise.VmHarness.Evidence;

public static class VmHarnessGuestEvidenceCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task CaptureBeforeMutationAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken)
    {
        await CaptureSnapshotAsync(context, "before", cancellationToken);
    }

    public static async Task CaptureAfterMutationAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken)
    {
        await CaptureSnapshotAsync(context, "after", cancellationToken);
    }

    private static async Task CaptureSnapshotAsync(
        VmHarnessScenarioContext context,
        string phase,
        CancellationToken cancellationToken)
    {
        if (context.GuestTransport is null
            || !context.GuestTransport.IsConfigured
            || string.IsNullOrWhiteSpace(context.LabDatabasePath))
        {
            return;
        }

        var guestTarget = VmHarnessGuestTargetFactory.FromVmTarget(context.Target);
        var dbArg = VmHarnessLabDatabaseResolver.ToGuestDbArgument(context.LabDatabasePath);
        var snapshotResult = await context.GuestTransport.ExecuteCommandAsync(
            guestTarget,
            VmHarnessGuestEvidenceCommands.LabEvidenceSnapshot(dbArg),
            VmHarnessLabEnvironment.MutationCommandEnvironment,
            cancellationToken);

        if (snapshotResult.ExitCode != 0)
        {
            await context.RunStore.WriteSanitizedLogAsync(
                context.Run,
                $"guest-evidence-{phase}.log",
                $"{snapshotResult.StandardOutput}\n{snapshotResult.StandardError}",
                cancellationToken);
            return;
        }

        using var document = JsonDocument.Parse(snapshotResult.StandardOutput);
        var root = document.RootElement;

        if (root.TryGetProperty("recommendationScan", out var recommendationScan)
            && recommendationScan.ValueKind != JsonValueKind.Null)
        {
            await context.RunStore.WriteTextArtifactAsync(
                context.Run,
                $"recommendation-{phase}.json",
                recommendationScan.GetRawText(),
                cancellationToken);
        }

        if (root.TryGetProperty("recommendationScan", out var wuaScan)
            && wuaScan.ValueKind != JsonValueKind.Null
            && wuaScan.TryGetProperty("unmatchedUpdates", out var unmatchedUpdates))
        {
            await context.RunStore.WriteTextArtifactAsync(
                context.Run,
                $"wua-{phase}.json",
                unmatchedUpdates.GetRawText(),
                cancellationToken);
        }

        if (root.TryGetProperty("devices", out var devices))
        {
            await context.RunStore.WriteTextArtifactAsync(
                context.Run,
                $"device-{phase}.json",
                devices.GetRawText(),
                cancellationToken);
        }

        if (root.TryGetProperty("transactions", out var transactions))
        {
            await context.RunStore.WriteTextArtifactAsync(
                context.Run,
                $"transactions-{phase}.json",
                transactions.GetRawText(),
                cancellationToken);
        }

        if (root.TryGetProperty("plans", out var plans))
        {
            await context.RunStore.WriteTextArtifactAsync(
                context.Run,
                $"plans-{phase}.json",
                plans.GetRawText(),
                cancellationToken);
        }
    }
}
