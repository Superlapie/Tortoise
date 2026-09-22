using System.Runtime.CompilerServices;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Core.Tests;

internal static class LabTestGate
{
    [ModuleInitializer]
    internal static void EnableLabBuild() => MutationBuildPolicy.EnableTestLabMode();
}
