using Tortoise.Broker.Ipc;
using Tortoise.Broker.Serialization;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Security.Broker;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: tortoise-broker-pipe-probe <pipeName> <sessionId> <capabilityToken> [connectTimeoutMs]");
    return 1;
}

var pipeName = args[0];
if (!int.TryParse(args[1], out var sessionId))
{
    Console.Error.WriteLine("sessionId must be an integer.");
    return 1;
}

var capabilityToken = args[2];
var connectTimeoutMs = args.Length >= 4 && int.TryParse(args[3], out var parsedTimeout)
    ? parsedTimeout
    : 5000;

var options = new BrokerHostOptions
{
    SessionId = sessionId,
    CapabilityToken = capabilityToken,
    PipeName = pipeName,
    ConnectTimeoutMs = connectTimeoutMs,
};

var request = BrokerRequestFactory.Create(BrokerOperation.Ping, sessionId, capabilityToken);
var client = new BrokerPipeClient();

try
{
    var response = await client.SendAsync(options, request);
    Console.WriteLine($"{response.Succeeded}|{response.ErrorCode}|{response.Message}");
    return response.Succeeded ? 0 : 3;
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR|{ex.GetType().Name}|{ex.Message}");
    return 4;
}
