using Tortoise.Contracts.Mutation;

namespace Tortoise.Core.Mutation;

public static class MutationCapabilityResolver
{
    public static IMutationCapability Resolve(ExecutionEnvironmentInfo environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDisposableVm
            && environment.MutationTestsEnabled
            && environment.VmInstallExplicitlyAllowed)
        {
            return new MutationCapability(
                isEnabled: true,
                environment: MutationEnvironment.DisposableVm,
                reason: "Disposable VM install enabled via explicit environment markers.");
        }

        return MutationCapability.ReadOnly;
    }
}
