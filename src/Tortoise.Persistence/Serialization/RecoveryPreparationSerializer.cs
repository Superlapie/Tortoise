using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.Core.Recovery;
using Tortoise.Core.Updates;

namespace Tortoise.Persistence.Serialization;

internal static class RecoveryPreparationSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static string Serialize(RecoveryPreparationRecord record) =>
        JsonSerializer.Serialize(record, SerializerOptions);

    public static RecoveryPreparationRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<RecoveryPreparationRecord>(json, SerializerOptions)
        ?? throw new InvalidOperationException("Recovery preparation payload could not be deserialized.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new VersionJsonConverter());
        options.Converters.Add(new DateOnlyJsonConverter());

        return options;
    }

    private sealed class VersionJsonConverter : JsonConverter<Version>
    {
        public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Version.Parse(reader.GetString() ?? "0.0");

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateOnly.Parse(reader.GetString() ?? throw new JsonException("Expected date string."));

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("O"));
    }
}
