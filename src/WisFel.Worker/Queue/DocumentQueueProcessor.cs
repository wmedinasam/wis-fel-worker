using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WisFel.Worker.Api;
using WisFel.Worker.Data;
using WisFel.Worker.Sae;

namespace WisFel.Worker.Queue;

/// <summary>
/// Reads the local queue, builds the provisional request JSON for each document and reports its metadata to
/// the API. It does not change `Status`: there is no certifier contract yet, so a document stays `Pending`
/// with its `Request` stored, which is also how the next pass knows it is already prepared.
/// </summary>
public sealed class DocumentQueueProcessor(
    FelDbContext felDbContext,
    ISaeDocumentReader documentReader,
    IDocumentSendPolicy sendPolicy,
    IFelApiClient felApiClient,
    IOptions<WorkerOptions> options,
    ILogger<DocumentQueueProcessor> logger) : IDocumentQueueProcessor
{
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var companies = await felDbContext.SaeCompanies.ToListAsync(cancellationToken);
        var prepared = 0;

        foreach (var company in companies)
        {
            var decision = sendPolicy.Evaluate(company);
            if (!decision.IsAllowed)
            {
                logger.LogDebug(
                    "Company {CompanyId} is skipped: {DenialReason}.",
                    company.Id,
                    decision.DenialReason);
                continue;
            }

            var documents = await felDbContext.Documents
                .Where(document => document.CompanyId == company.Id &&
                                   document.Status == DocumentStatus.Pending &&
                                   document.Request == null)
                .OrderBy(document => document.Id)
                .Take(options.Value.BatchSize)
                .ToListAsync(cancellationToken);

            if (documents.Count == 0)
            {
                continue;
            }

            var readDocuments = new List<Document>(documents.Count);
            foreach (var document in documents)
            {
                try
                {
                    await documentReader.BuildRequestJsonAsync(company, document, cancellationToken);
                    readDocuments.Add(document);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // shortcut: a document whose SAE read keeps failing is retried on every pass and never
                    // marked Failed; add a retry cut-off once the queue has an attempt or timestamp column.
                    // The message is logged without the query text or the payload.
                    logger.LogWarning(
                        "Document {IdempotencyKey} could not be read from SAE: {Reason}",
                        DocumentStatusReportMapper.IdempotencyKey(document),
                        exception.Message);
                }
            }

            if (readDocuments.Count == 0)
            {
                continue;
            }

            await felDbContext.SaveChangesAsync(cancellationToken);
            prepared += readDocuments.Count;
            await ReportAsync(company.Id, readDocuments, cancellationToken);
        }

        return prepared;
    }

    private async Task ReportAsync(
        int companyId,
        IReadOnlyList<Document> documents,
        CancellationToken cancellationToken)
    {
        var reports = documents.Select(document => DocumentStatusReportMapper.Create(document)).ToArray();

        try
        {
            var response = await felApiClient.ReportDocumentStatusAsync(
                companyId.ToString(CultureInfo.InvariantCulture),
                reports,
                cancellationToken);

            foreach (var rejected in response.Rejected)
            {
                logger.LogWarning(
                    "The API rejected the status of {IdempotencyKey}: {Reason}",
                    rejected.IdempotencyKey,
                    rejected.Reason);
            }
        }
        catch (FelApiException exception)
        {
            // The request JSON is already stored; only the metadata report is lost for now.
            logger.LogWarning(
                "The status of {Count} documents of company {CompanyId} could not be reported ({StatusCode}).",
                reports.Length,
                companyId,
                exception.StatusCode);
        }
    }
}
