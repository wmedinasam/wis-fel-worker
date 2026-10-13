using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WisFel.Worker.Payloads;

public sealed class DocumentRequest
{
    [JsonPropertyName("Document")]
    public DocumentPayload Document { get; init; } = new();
}

public sealed class DocumentPayload
{
    [JsonPropertyName("Issuer")]
    public JsonObject Issuer { get; init; } = new();

    [JsonPropertyName("Receiver")]
    public JsonObject Receiver { get; init; } = new();

    [JsonPropertyName("Items")]
    public IReadOnlyList<JsonObject> Items { get; init; } = [];

    [JsonPropertyName("FreeFields")]
    public SortedDictionary<string, JsonNode?> FreeFields { get; init; } =
        new(StringComparer.Ordinal);
}
