using System.Text.Json;
using WisFel.Worker.Api;
using WisFel.Worker.Data;

namespace WisFel.Worker.Tests;

public class DocumentStatusReportTests
{
    [Fact]
    public void IdempotencyKeyUsesTheQueueNaturalKey()
    {
        var document = TestDocuments.Create(status: DocumentStatus.Pending);

        Assert.Equal("7-3-FACT-4521", DocumentStatusReportMapper.IdempotencyKey(document));
    }

    [Theory]
    [InlineData(DocumentStatus.Pending, false, "Pending")]
    [InlineData(DocumentStatus.Processing, false, "Processing")]
    [InlineData(DocumentStatus.Signed, false, "Certified")]
    [InlineData(DocumentStatus.Delivered, false, "Certified")]
    [InlineData(DocumentStatus.Rejected, false, "Error")]
    [InlineData(DocumentStatus.Failed, false, "Error")]
    [InlineData(DocumentStatus.Failed, true, "Retrying")]
    public void LocalStatusesTranslateToTheApiEnum(DocumentStatus status, bool canRetry, string expected)
    {
        Assert.Equal(expected, DocumentStatusReportMapper.ToApiStatus(status, canRetry));
    }

    [Fact]
    public void ReportCarriesSatMetadataAndNoBranchMapping()
    {
        var certifiedAt = DateTimeOffset.Parse("2026-10-20T15:00:02Z");
        var document = TestDocuments.Create(
            status: DocumentStatus.Signed,
            uuid: "3F2504E0-4F89-11D3-9A0C-0305E82C3301",
            serial: "A",
            number: "123",
            observations: "Certificado");

        var report = DocumentStatusReportMapper.Create(
            document,
            attemptCount: 2,
            issuedAtUtc: certifiedAt.AddSeconds(-2),
            certifiedAtUtc: certifiedAt,
            errorCode: null);

        Assert.Equal("7-3-FACT-4521", report.IdempotencyKey);
        Assert.Equal("FACT", report.DocumentType);
        Assert.Equal("Certified", report.Status);
        Assert.Equal("3F2504E0-4F89-11D3-9A0C-0305E82C3301", report.AuthorizationNumber);
        Assert.Equal("A", report.Series);
        Assert.Equal("123", report.Number);
        Assert.Equal("Certificado", report.ErrorMessage);
        Assert.Equal(2, report.AttemptCount);
        Assert.Equal(certifiedAt, report.CertifiedAtUtc);
        Assert.Null(report.BranchId);
    }

    [Fact]
    public void ReportNeverCarriesTheSignedPayload()
    {
        var document = TestDocuments.Create(status: DocumentStatus.Signed, request: """{"Document":{}}""");
        document.StoreRequestJson("""{"Document":{"Issuer":{}}}""");

        var json = JsonSerializer.Serialize(
            DocumentStatusReportMapper.Create(document),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("request", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pdf", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Issuer", json, StringComparison.Ordinal);
    }

}
