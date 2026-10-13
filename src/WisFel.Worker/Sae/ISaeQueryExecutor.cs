using System.Text.Json.Nodes;
using WisFel.Worker.Data;

namespace WisFel.Worker.Sae;

public interface ISaeQueryExecutor
{
    Task<IReadOnlyList<JsonObject>> ExecuteJsonObjectsAsync(
        string connectionString,
        string query,
        Document document,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SaeFreeFieldValue>> ExecuteFreeFieldsAsync(
        string connectionString,
        string query,
        Document document,
        CancellationToken cancellationToken);
}
