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

        var commonQueries = await felDbContext.SaeCommonQueries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                query => query.CompanyId == company.Id &&
                         query.SaeVersion == company.SaeVersion &&
                         query.IsEnabled,
                cancellationToken);

        if (commonQueries is null)
        {
            throw new InvalidOperationException(
                $"No enabled common SAE query configuration exists for company {company.Id} " +
                $"and version {company.SaeVersion}.");
        }

        var documentQueries = await felDbContext.SaeDocumentQueries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                query => query.CompanyId == company.Id &&
                         query.SaeVersion == company.SaeVersion &&
                         query.DocumentType == document.DocumentType &&
                         query.IsEnabled,
                cancellationToken);

        if (documentQueries is null)
        {
            throw new InvalidOperationException(
                $"No enabled SAE query template exists for company {company.Id}, " +
                $"version {company.SaeVersion} and document type '{document.DocumentType}'.");
        }

        var saeConnectionString = SaeConnectionStringFactory.Create(sharedConnectionString, company);
        var documentFields = await ReadSingleObjectAsync(
            saeConnectionString,
            documentQueries.DocumentSql,
            document,
            "Document",
            cancellationToken);
        var issuer = await ReadSingleObjectAsync(
            saeConnectionString,
            documentQueries.IssuerSql,
            document,
            "Issuer",
            cancellationToken);
        var receiver = await ReadSingleObjectAsync(
            saeConnectionString,
            commonQueries.CustomerSql,
            document,
            "Customer",
            cancellationToken);
        var itemRows = await queryExecutor.ExecuteJsonObjectsAsync(
            saeConnectionString,
            documentQueries.ItemsSql,
            document,
            cancellationToken);
        var productsByCode = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in itemRows)
        {
            var productCode = SaeItemProductMapping.GetProductCode(item);
            if (!productsByCode.TryGetValue(productCode, out var product))
            {
                product = await ReadSingleObjectAsync(
                    saeConnectionString,
                    commonQueries.ProductSql,
                    document,
                    "Product",
                    cancellationToken,
                    productCode);
                productsByCode.Add(productCode, product);
            }

            SaeItemProductMapping.SetProduct(item, product.DeepClone().AsObject());
        }

        var freeFields = await queryExecutor.ExecuteFreeFieldsAsync(
            saeConnectionString,
            documentQueries.FreeFieldsSql,
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
        CancellationToken cancellationToken,
        string? productCode = null)
    {
        var rows = await queryExecutor.ExecuteJsonObjectsAsync(
            connectionString,
            query,
            document,
            cancellationToken,
            productCode);

        if (rows.Count != 1)
        {
            throw new InvalidOperationException(
                $"SAE {section} query must return exactly one JSON object; it returned {rows.Count} rows.");
        }

        return rows[0];
    }
}
