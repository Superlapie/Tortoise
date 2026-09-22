using Tortoise.Contracts.Mutation;

Console.WriteLine("Tortoise CLI (read-only preview)");
Console.WriteLine($"Mutation enabled: {MutationCapability.ReadOnly.IsEnabled}");
Console.WriteLine($"Environment: {MutationCapability.ReadOnly.Environment}");
Console.WriteLine("Use 'tortoise scan' and related commands once Batch 4+ land.");
