using System.Data;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using WisFel.Worker.Data;

namespace WisFel.Worker.Sae;

public sealed class SqlSaeQueryExecutor : ISaeQueryExecutor
{
    public async Task<IReadOnlyList<JsonObject>> ExecuteJsonObjectsAsync(
        string connectionString,
        string query,
        Document document,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = CreateCommand(connection, query, document);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        RequireColumns(reader, SaeQueryResultContract.JsonColumn);

        var objects = new List<JsonObject>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (await reader.IsDBNullAsync(0, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"SAE query result column '{SaeQueryResultContract.JsonColumn}' cannot be NULL.");
            }

            var json = reader.GetString(0);
            if (JsonNode.Parse(json) is not JsonObject jsonObject)
            {
                throw new InvalidOperationException(
                    $"SAE query result column '{SaeQueryResultContract.JsonColumn}' must contain a JSON object.");
            }

            objects.Add(jsonObject);
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            throw new InvalidOperationException("SAE query must return a single result set.");
        }

        return objects;
    }

    public async Task<IReadOnlyList<SaeFreeFieldValue>> ExecuteFreeFieldsAsync(
        string connectionString,
        string query,
        Document document,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = CreateCommand(connection, query, document);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        RequireColumns(
            reader,
            SaeQueryResultContract.FreeFieldNameColumn,
            SaeQueryResultContract.FreeFieldValueColumn);

        var fields = new List<SaeFreeFieldValue>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (await reader.IsDBNullAsync(0, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"SAE query result column '{SaeQueryResultContract.FreeFieldNameColumn}' cannot be NULL.");
            }

            fields.Add(new SaeFreeFieldValue(
                reader.GetString(0),
                await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1)));
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            throw new InvalidOperationException("SAE query must return a single result set.");
        }

        return fields;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string query, Document document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(document);

        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = query;
        command.CommandTimeout = 30;
        command.Parameters.AddRange(CreateParameters(
            document.InternalId,
            document.InternalNumber,
            document.InternalSerial,
            document.LocationId,
            document.DocumentType));

        return command;
    }

    public static SqlParameter[] CreateParameters(
        int internalId,
        string internalNumber,
        string internalSerial,
        int locationId,
        string documentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(internalSerial);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);

        return
        [
            new SqlParameter("@InternalId", SqlDbType.Int) { Value = internalId },
            new SqlParameter("@InternalNumber", SqlDbType.NVarChar, 50) { Value = internalNumber },
            new SqlParameter("@InternalSerial", SqlDbType.NVarChar, 20) { Value = internalSerial },
            new SqlParameter("@LocationId", SqlDbType.Int) { Value = locationId },
            new SqlParameter("@DocumentType", SqlDbType.NVarChar, 10)
            {
                Value = documentType.Trim().ToUpperInvariant()
            }
        ];
    }

    private static void RequireColumns(SqlDataReader reader, params string[] expectedColumns)
    {
        if (reader.FieldCount != expectedColumns.Length)
        {
            throw new InvalidOperationException(
                $"SAE query returned {reader.FieldCount} columns; expected {expectedColumns.Length}.");
        }

        for (var index = 0; index < expectedColumns.Length; index++)
        {
            if (!string.Equals(reader.GetName(index), expectedColumns[index], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SAE query column {index + 1} must be aliased as '{expectedColumns[index]}'.");
            }
        }
    }
}
