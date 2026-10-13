using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WisFel.Worker.Data;
using WisFel.Worker.Payloads;

namespace WisFel.Worker.Sae;

public sealed class SaeDocumentReader(
    FelDbContext felDbContext,
    IConfiguration configuration,
    ISaeQueryExecutor queryExecutor,
    IDocumentSendPolicy sendPolicy) : ISaeDocumentReader
{
    public async Task<string> BuildRequestJsonAsync(
        SaeCompany company,
        Document document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(document);

        if (document.CompanyId != company.Id)
        {
            throw new ArgumentException(
                "The document does not belong to the supplied SAE company.",
                nameof(document));
        }

        if (document.Status != DocumentStatus.Pending)
        {
            throw new InvalidOperationException("Only Pending documents can be read from SAE.");
        }

        var sendDecision = sendPolicy.Evaluate(company);
        if (!sendDecision.IsAllowed)
        {
            throw new InvalidOperationException(
                $"Document cannot be read for sending: {sendDecision.DenialReason}.");
        }

        var sharedConnectionString = configuration.GetConnectionString("FEL");
        if (string.IsNullOrWhiteSpace(sharedConnectionString))
        {
            throw new InvalidOperationException("Missing ConnectionStrings:FEL.");
        }

        var queryTemplate = await felDbContext.SaeDocumentQueries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                query => query.CompanyId == company.Id &&
                         query.SaeVersion == company.SaeVersion &&
                         query.DocumentType == document.DocumentType &&
                         query.IsEnabled,
                cancellationToken);

        if (queryTemplate is null)
        {
            throw new InvalidOperationException(
                $"No enabled SAE query template exists for company {company.Id}, " +
                $"version {company.SaeVersion} and document type '{document.DocumentType}'.");
        }

        var saeConnectionString = SaeConnectionStringFactory.Create(sharedConnectionString, company);
        var documentFields = await ReadSingleObjectAsync(
            saeConnectionString,
            queryTemplate.DocumentSql,
            document,
            "Document",
            cancellationToken);
        var issuer = await ReadSingleObjectAsync(
            saeConnectionString,
            queryTemplate.IssuerSql,
            document,
            "Issuer",
            cancellationToken);
        var receiver = await ReadSingleObjectAsync(
            saeConnectionString,
            queryTemplate.ReceiverSql,
            document,
            "Receiver",
            cancellationToken);
        var itemRows = await queryExecutor.ExecuteJsonObjectsAsync(
            saeConnectionString,
            queryTemplate.ItemsSql,
            document,
            cancellationToken);
        var freeFields = await queryExecutor.ExecuteFreeFieldsAsync(
            saeConnectionString,
            queryTemplate.FreeFieldsSql,
            document,
            cancellationToken);

        var freeFieldMap = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var field in freeFields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !freeFieldMap.TryAdd(field.Name, JsonValue.Create(field.Value)))
            {
                throw new InvalidOperationException(
                    "SAE FreeFields query must return non-empty, unique Name values.");
            }
        }

        var request = new DocumentRequest
        {
            Document = new DocumentPayload
            {
                Fields = documentFields,
                Issuer = issuer,
                Receiver = receiver,
                Items = itemRows,
                FreeFields = freeFieldMap
            }
        };
        var requestJson = DocumentRequestJsonBuilder.Serialize(request);
        document.StoreRequestJson(requestJson);
        return requestJson;
    }

    private async Task<JsonObject> ReadSingleObjectAsync(
        string connectionString,
        string query,
        Document document,
        string section,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.ExecuteJsonObjectsAsync(
            connectionString,
            query,
            document,
            cancellationToken);

        if (rows.Count != 1)
        {
            throw new InvalidOperationException(
                $"SAE {section} query must return exactly one JSON object; it returned {rows.Count} rows.");
        }

        return rows[0];
    }
}
