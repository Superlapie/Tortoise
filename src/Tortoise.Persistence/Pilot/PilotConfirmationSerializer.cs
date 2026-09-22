using System.Text.Json;
using Tortoise.Core.Pilot;

namespace Tortoise.Persistence.Pilot;

internal static class PilotConfirmationSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string SerializeAcknowledgements(IReadOnlyList<string> itemIds) =>
        JsonSerializer.Serialize(itemIds, Options);

    public static IReadOnlyList<string> DeserializeAcknowledgements(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];
}
