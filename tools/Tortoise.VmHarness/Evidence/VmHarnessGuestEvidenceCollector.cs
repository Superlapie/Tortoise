using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Storage;

using Tortoise.VmHarness.Scenarios;

namespace Tortoise.VmHarness.Evidence;

public static class VmHarnessGuestEvidenceCollector
{
    public static async Task CaptureBeforeMutationAsync(
        VmHarnessScenarioContext context,
        string guestReportPath,
        CancellationToken cancellationToken)
    {
        if (context.GuestTransport is null
            || !context.GuestTransport.IsConfigured
            || string.IsNullOrWhiteSpace(context.LabDatabasePath))
        {
            return;
        }

        var dbArg = VmHarnessLabDatabaseResolver.ToGuestDbArgument(context.LabDatabasePath);
        var export = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            VmHarnessGuestEvidenceCommands.ExportReport(guestReportPath, dbArg),
            LabEnvironmentVariables(),
            cancellationToken);
        if (export.ExitCode == 0)
        {
            var read = await context.GuestTransport.ExecuteCommandAsync(
                context.Target.Name,
                $"cmd /c type \"{guestReportPath}\"",
                null,
                cancellationToken);
            if (read.ExitCode == 0)
            {
                await context.RunStore.WriteTextArtifactAsync(
                    context.Run,
                    "device-before.json",
                    read.StandardOutput,
                    cancellationToken);
                await context.RunStore.WriteTextArtifactAsync(
                    context.Run,
                    "wua-before.json",
                    read.StandardOutput,
                    cancellationToken);
            }
        }

        var transactions = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            VmHarnessGuestEvidenceCommands.RecoverList(dbArg),
            LabEnvironmentVariables(),
            cancellationToken);
        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "transactions-before.json",
            $"{transactions.StandardOutput}\n{transactions.StandardError}",
            cancellationToken);
    }

    public static async Task CaptureAfterMutationAsync(
        VmHarnessScenarioContext context,
        string guestReportPath,
        CancellationToken cancellationToken)
    {
        if (context.GuestTransport is null
            || !context.GuestTransport.IsConfigured
            || string.IsNullOrWhiteSpace(context.LabDatabasePath))
        {
            return;
        }

        var dbArg = VmHarnessLabDatabaseResolver.ToGuestDbArgument(context.LabDatabasePath);
        var export = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            VmHarnessGuestEvidenceCommands.ExportReport(guestReportPath, dbArg),
            LabEnvironmentVariables(),
            cancellationToken);
        if (export.ExitCode == 0)
        {
            var read = await context.GuestTransport.ExecuteCommandAsync(
                context.Target.Name,
                $"cmd /c type \"{guestReportPath}\"",
                null,
                cancellationToken);
            if (read.ExitCode == 0)
            {
                await context.RunStore.WriteTextArtifactAsync(
                    context.Run,
                    "device-after.json",
                    read.StandardOutput,
                    cancellationToken);
                await context.RunStore.WriteTextArtifactAsync(
                    context.Run,
                    "wua-after.json",
                    read.StandardOutput,
                    cancellationToken);
            }
        }

        var transactions = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            VmHarnessGuestEvidenceCommands.RecoverList(dbArg),
            LabEnvironmentVariables(),
            cancellationToken);
        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "transactions-after.json",
            $"{transactions.StandardOutput}\n{transactions.StandardError}",
            cancellationToken);
    }

    private static Dictionary<string, string> LabEnvironmentVariables() =>
        new()
        {
            ["TORTOISE_MUTATION_TESTS"] = "1",
            ["TORTOISE_ALLOW_VM_INSTALL"] = "1",
        };
}
