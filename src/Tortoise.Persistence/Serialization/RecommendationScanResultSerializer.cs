using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.Core.Recommendations;

namespace Tortoise.Persistence.Serialization;

internal static class RecommendationScanResultSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static string Serialize(RecommendationScanResult result) =>
        JsonSerializer.Serialize(result, SerializerOptions);

    public static RecommendationScanResult Deserialize(string json) =>
        JsonSerializer.Deserialize<RecommendationScanResult>(json, SerializerOptions)
        ?? throw new InvalidOperationException("Scan session payload could not be deserialized.");

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
