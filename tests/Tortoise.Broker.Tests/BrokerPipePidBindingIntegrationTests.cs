using System.Diagnostics;
using System.Runtime.Versioning;
using Tortoise.Broker.Ipc;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Tests;

public sealed class BrokerPipePidBindingIntegrationTests
{
    [Fact]
    public async Task Wrong_pid_rejection_does_not_consume_authorized_client_connection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var probePath = LocateBrokerPipeProbe();
        if (probePath is null)
        {
            throw new InvalidOperationException("tortoise-broker-pipe-probe was not found.");
        }

        var sessionId = Random.Shared.Next(1000, 9999);
        var capability = Guid.NewGuid().ToString("N");
        var pipeName = ElevationConstants.GetPipeName(sessionId, capability);
        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            CapabilityToken = capability,
            PipeName = pipeName,
            ConnectTimeoutMs = 5000,
            AuthorizedClientProcessId = Environment.ProcessId,
        };

        var serverTask = BrokerHost.RunOnceAsync(options);
        await Task.Delay(200);

        using var wrongPidProbe = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{probePath}\" \"{pipeName}\" {sessionId} {capability} 5000",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start broker pipe probe.");

        var wrongPidOutput = await wrongPidProbe.StandardOutput.ReadToEndAsync();
        await wrongPidProbe.WaitForExitAsync();

        Assert.Equal(3, wrongPidProbe.ExitCode);
        Assert.Contains("ClientProcessMismatch", wrongPidOutput, StringComparison.Ordinal);

        var authorizedClient = new BrokerPipeClient();
        var response = await authorizedClient.SendAsync(
            options,
            BrokerRequestFactory.Create(BrokerOperation.Ping, sessionId, capability));

        Assert.True(response.Succeeded);
        await serverTask;
    }

    private static string? LocateBrokerPipeProbe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tortoise-broker-pipe-probe.dll"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Tortoise.BrokerPipeProbe",
                "bin",
                "Debug",
                "net10.0",
                "tortoise-broker-pipe-probe.dll")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
