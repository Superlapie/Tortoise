using Tortoise.Contracts.Mutation;

namespace Tortoise.Core.Mutation;

public static class MutationGuard
{
    public static void RequireMutationEnabled(IMutationCapability capability, string operationName)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (!capability.IsEnabled)
        {
            throw new MutationDeniedException(
                $"Operation '{operationName}' was denied: {capability.Reason}");
        }
    }
}
