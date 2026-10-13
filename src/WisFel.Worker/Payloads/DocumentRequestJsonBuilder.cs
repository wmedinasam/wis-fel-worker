using System.Text.Json;

namespace WisFel.Worker.Payloads;

public static class DocumentRequestJsonBuilder
{
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

        return JsonSerializer.Serialize(request, SerializerOptions);
    }
}
