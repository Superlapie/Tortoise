using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.Contracts.Elevation;

namespace Tortoise.Broker.Serialization;

internal static class BrokerMessageSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string SerializeRequest(BrokerRequest request) =>
        JsonSerializer.Serialize(request, Options);

    public static string SerializeResponse(BrokerResponse response) =>
        JsonSerializer.Serialize(response, Options);

    public static BrokerRequest DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<BrokerRequest>(json, Options)
        ?? throw new InvalidOperationException("Broker request payload could not be deserialized.");

    public static BrokerResponse DeserializeResponse(string json) =>
        JsonSerializer.Deserialize<BrokerResponse>(json, Options)
        ?? throw new InvalidOperationException("Broker response payload could not be deserialized.");

    public static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await ReadExactAsync(stream, lengthBuffer, cancellationToken);
        var length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > 1_048_576)
        {
            throw new InvalidOperationException("Broker message length is invalid.");
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken);
        return Encoding.UTF8.GetString(payload);
    }

    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Broker connection closed before message completed.");
            }

            offset += read;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
