using System.Globalization;
using WisFel.Worker.Data;

namespace WisFel.Worker.Api;

/// <summary>Translates the local queue row into API metadata. Request, Response and Pdf never leave the worker.</summary>
public static class DocumentStatusReportMapper
{
    public const int MinBatchSize = 1;

    public const int MaxBatchSize = 100;

    /// <summary>The queue's natural key, the same one the unique index uses to stop duplicate trigger inserts.</summary>
    public static string IdempotencyKey(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{document.CompanyId}-{document.LocationId}-{document.DocumentType}-{document.InternalId}");
    }

    public static string ToApiStatus(DocumentStatus status, bool canRetry) => status switch
    {
        DocumentStatus.Pending => "Pending",
        DocumentStatus.Processing => "Processing",
        DocumentStatus.Signed or DocumentStatus.Delivered => "Certified",
        DocumentStatus.Rejected => "Error",
        DocumentStatus.Failed => canRetry ? "Retrying" : "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown local document status.")
    };

    public static DocumentStatusReport Create(
        Document document,
        bool canRetry = false,
        int attemptCount = 1,
        DateTimeOffset? issuedAtUtc = null,
        DateTimeOffset? certifiedAtUtc = null,
        string? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(attemptCount);

        return new DocumentStatusReport
        {
            IdempotencyKey = IdempotencyKey(document),
            // LocationId has no agreed mapping to an API branch yet.
            BranchId = null,
            DocumentType = document.DocumentType,
            Series = document.Serial,
            Number = document.Number,
            Status = ToApiStatus(document.Status, canRetry),
            AuthorizationNumber = document.Uuid,
            ErrorCode = errorCode,
            ErrorMessage = document.Observations,
            AttemptCount = attemptCount,
            IssuedAtUtc = issuedAtUtc,
            CertifiedAtUtc = certifiedAtUtc
        };
    }
}
