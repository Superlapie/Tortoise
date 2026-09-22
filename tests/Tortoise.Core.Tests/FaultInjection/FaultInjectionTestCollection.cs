namespace Tortoise.Core.Tests.FaultInjection;

[CollectionDefinition(nameof(FaultInjectionEnvironment), DisableParallelization = true)]
public sealed class FaultInjectionEnvironment : ICollectionFixture<object>;
