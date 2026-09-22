using System.Runtime.CompilerServices;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Broker.Tests;

internal static class LabTestGate
{
    [ModuleInitializer]
    internal static void EnableLabBuild() => MutationBuildPolicy.EnableTestLabMode();
}
