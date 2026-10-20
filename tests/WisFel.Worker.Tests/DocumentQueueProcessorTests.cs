using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WisFel.Worker.Api;
using WisFel.Worker.Data;
using WisFel.Worker.Queue;
using WisFel.Worker.Sae;

namespace WisFel.Worker.Tests;

public class DocumentQueueProcessorTests
{
    [Fact]
    public async Task OnlyPendingDocumentsWithoutRequestArePrepared()
    {
        var databaseName = Guid.NewGuid().ToString();
        var company = await SeedAsync(databaseName, licensed: true, documents:
        [
            TestDocuments.Create(internalId: 1),
            TestDocuments.Create(internalId: 2),
            TestDocuments.Create(internalId: 3, request: """{"Document":{}}"""),
            TestDocuments.Create(internalId: 4, status: DocumentStatus.Signed)
        ]);
        var reader = new FakeSaeDocumentReader();
        var client = new FakeFelApiClient();

        var prepared = await CreateProcessor(databaseName, reader, client).ProcessPendingAsync();

        Assert.Equal(2, prepared);
        Assert.Equal([1, 2], reader.ReadInternalIds);
        await using var context = CreateContext(databaseName);
        var stored = await context.Documents.Where(document => document.Request != null).ToListAsync();
        Assert.Equal(3, stored.Count);
        Assert.All(
            stored.Where(document => document.InternalId is 1 or 2),
            document => Assert.Equal(DocumentStatus.Pending, document.Status));

        var (companyKey, reports) = Assert.Single(client.Reports);
        Assert.Equal(company.Id.ToString(), companyKey);
        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Equal("Pending", report.Status));
        Assert.Contains($"{company.Id}-3-FACT-1", reports.Select(report => report.IdempotencyKey));
    }

    [Fact]
    public async Task AnUnlicensedCompanyIsSkipped()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName, licensed: false, documents: [TestDocuments.Create(internalId: 1)]);
        var reader = new FakeSaeDocumentReader();
        var client = new FakeFelApiClient();

        var prepared = await CreateProcessor(databaseName, reader, client).ProcessPendingAsync();

        Assert.Equal(0, prepared);
        Assert.Empty(reader.ReadInternalIds);
        Assert.Empty(client.Reports);
    }

    [Fact]
    public async Task AFailingDocumentDoesNotStopTheRest()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName, licensed: true, documents:
        [
            TestDocuments.Create(internalId: 1),
            TestDocuments.Create(internalId: 2)
        ]);
        var reader = new FakeSaeDocumentReader { FailingInternalId = 1 };
        var client = new FakeFelApiClient();

        var prepared = await CreateProcessor(databaseName, reader, client).ProcessPendingAsync();

        Assert.Equal(1, prepared);
        var (_, reports) = Assert.Single(client.Reports);
        Assert.Single(reports);
        await using var context = CreateContext(databaseName);
        Assert.Null((await context.Documents.SingleAsync(document => document.InternalId == 1)).Request);
        Assert.NotNull((await context.Documents.SingleAsync(document => document.InternalId == 2)).Request);
    }

    [Fact]
    public async Task TheBatchSizeLimitsOnePass()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName, licensed: true, documents:
        [
            TestDocuments.Create(internalId: 1),
            TestDocuments.Create(internalId: 2),
            TestDocuments.Create(internalId: 3)
        ]);
        var reader = new FakeSaeDocumentReader();
        var client = new FakeFelApiClient();

        var prepared = await CreateProcessor(databaseName, reader, client, batchSize: 2).ProcessPendingAsync();

        Assert.Equal(2, prepared);
        Assert.Equal([1, 2], reader.ReadInternalIds);
    }

    [Fact]
    public async Task AFailedReportKeepsTheStoredRequest()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName, licensed: true, documents: [TestDocuments.Create(internalId: 1)]);
        var client = new FakeFelApiClient
        {
            ReportFailure = new FelApiException(
                "agent/documents/status",
                "1",
                HttpStatusCode.ServiceUnavailable,
                "unavailable")
        };

        var prepared = await CreateProcessor(databaseName, new FakeSaeDocumentReader(), client)
            .ProcessPendingAsync();

        Assert.Equal(1, prepared);
        await using var context = CreateContext(databaseName);
        Assert.NotNull((await context.Documents.SingleAsync()).Request);
    }

    private static DocumentQueueProcessor CreateProcessor(
        string databaseName,
        ISaeDocumentReader reader,
        IFelApiClient client,
        int batchSize = 50) =>
        new(
            CreateContext(databaseName),
            reader,
            new DocumentSendPolicy(),
            client,
            Options.Create(new WorkerOptions { BatchSize = batchSize }),
            NullLogger<DocumentQueueProcessor>.Instance);

    private static async Task<SaeCompany> SeedAsync(
        string databaseName,
        bool licensed,
        Document[] documents)
    {
        await using var context = CreateContext(databaseName);
        var company = new SaeCompany(4.6m, 1);
        if (licensed)
        {
            company.ApplyLicenseStatusFromCloud(true, DateTimeOffset.UtcNow);
        }

        context.SaeCompanies.Add(company);
        await context.SaveChangesAsync();

        foreach (var document in documents)
        {
            typeof(Document).GetProperty(nameof(Document.CompanyId))!.SetValue(document, company.Id);
            context.Documents.Add(document);
        }

        await context.SaveChangesAsync();
        return company;
    }

    private static FelDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FelDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private sealed class FakeSaeDocumentReader : ISaeDocumentReader
    {
        public List<int> ReadInternalIds { get; } = [];

        public int? FailingInternalId { get; init; }

        public Task<string> BuildRequestJsonAsync(
            SaeCompany company,
            Document document,
            CancellationToken cancellationToken = default)
        {
            if (document.InternalId == FailingInternalId)
            {
                throw new InvalidOperationException("No enabled SAE query template exists.");
            }

            ReadInternalIds.Add(document.InternalId);
            const string requestJson = """{"Document":{"Issuer":{}}}""";
            document.StoreRequestJson(requestJson);
            return Task.FromResult(requestJson);
        }
    }
}
