using System.Text.Json;
using System.Text.Json.Nodes;

namespace WisFel.Worker.Payloads;

public static class DocumentRequestJsonBuilder
{
    private static readonly HashSet<string> ReservedDocumentFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Issuer",
        "Receiver",
        "Items",
        "FreeFields"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static string Serialize(DocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.Document.Issuer);
        ArgumentNullException.ThrowIfNull(request.Document.Receiver);
        ArgumentNullException.ThrowIfNull(request.Document.Items);
        ArgumentNullException.ThrowIfNull(request.Document.FreeFields);

        if (request.Document.Items.Any(item => item is null))
        {
            throw new ArgumentException("Document items cannot contain null entries.", nameof(request));
        }

        var document = new JsonObject();
        foreach (var (name, value) in request.Document.Fields)
        {
            if (ReservedDocumentFields.Contains(name))
            {
                throw new ArgumentException(
                    $"Document fields cannot override the reserved '{name}' node.",
                    nameof(request));
            }

            document[name] = value?.DeepClone();
        }

        document["Issuer"] = request.Document.Issuer.DeepClone();
        document["Receiver"] = request.Document.Receiver.DeepClone();
        document["Items"] = new JsonArray(
            request.Document.Items.Select(item => (JsonNode?)item.DeepClone()).ToArray());

        var freeFields = new JsonObject();
        foreach (var (name, value) in request.Document.FreeFields)
        {
            freeFields[name] = value?.DeepClone();
        }

        document["FreeFields"] = freeFields;
        var root = new JsonObject { ["Document"] = document };

        return root.ToJsonString(SerializerOptions);
    }
}
