using Tortoise.Contracts.Mutation;

namespace Tortoise.Core.Mutation;

public static class MutationCapabilityResolver
{
    public static IMutationCapability Resolve(ExecutionEnvironmentInfo environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!MutationBuildPolicy.AllowsMutationCapabilityResolution)
        {
            return MutationCapability.ReadOnly;
        }

        if (environment.IsDisposableVm
            && environment.MutationTestsEnabled
            && environment.VmInstallExplicitlyAllowed)
        {
            return new MutationCapability(
                isEnabled: true,
                environment: MutationEnvironment.DisposableVm,
                reason: "Tortoise.Lab disposable VM install enabled via detected guest VM and explicit environment markers.");
        }

        return MutationCapability.ReadOnly;
    }
}
