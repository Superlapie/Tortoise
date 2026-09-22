using Tortoise.Contracts.Mutation;

namespace Tortoise.Core.Planning;

public sealed class SimulatedMutationWorkflowService : ISimulatedMutationWorkflowService
{
    private readonly ISimulatedUpdateTransactionService _simulatedTransactionService;

    public SimulatedMutationWorkflowService(ISimulatedUpdateTransactionService simulatedTransactionService)
    {
        _simulatedTransactionService = simulatedTransactionService;
    }

    public async Task<SimulatedMutationWorkflowResult> RunAsync(
        Guid planId,
        SimulatedMutationWorkflowOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var executionOptions = new SimulatedWorkflowExecutionOptions(
            options.RequireBrokerValidation,
            options.BrokerOptions);

        var simulation = await _simulatedTransactionService.SimulateFullWorkflowAsync(
            planId,
            MutationCapability.Simulated,
            executionOptions,
            cancellationToken);

        return new SimulatedMutationWorkflowResult(
            simulation.Transaction,
            simulation.Preflight,
            simulation.BrokerValidation,
            simulation.PackageVerification,
            simulation.PostInstallVerification,
            simulation.CompletedSuccessfully,
            simulation.Summary);
    }
}
