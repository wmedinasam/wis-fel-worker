using WisFel.Worker.Api;

namespace WisFel.Worker.Certifiers;

/// <summary>
/// What a provider needs to certify one document: the request JSON already built from SAE and the
/// configuration of the company. There is no canonical fiscal model yet, so the payload stays opaque.
/// </summary>
public sealed record CertificationRequest(
    string RequestJson,
    AgentCertifierConfigurationResponse Configuration);

/// <summary>
/// Outcome of one certification. The fields are the ones the worker already has to handle:
/// the authorization number and the error pair are what `POST /agent/documents/status` reports, and the
/// response payload and the PDF are the `dbo.Documents` columns. The caller timestamps it with its own clock.
/// </summary>
public sealed record CertificationResult(
    bool Succeeded,
    string? AuthorizationNumber,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResponsePayload,
    byte[]? Pdf)
{
    public static CertificationResult Certified(
        string authorizationNumber,
        string? responsePayload = null,
        byte[]? pdf = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationNumber);

        return new CertificationResult(true, authorizationNumber, null, null, responsePayload, pdf);
    }

    public static CertificationResult Failed(
        string errorCode,
        string? errorMessage = null,
        string? responsePayload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        return new CertificationResult(false, null, errorCode, errorMessage, responsePayload, null);
    }
}

/// <summary>
/// One certifier in one country. Every provider lives in this worker: the API never calls a certifier.
/// The implementation reads its endpoint, environment, format and credentials from
/// <see cref="CertificationRequest.Configuration"/> and never persists them.
/// </summary>
public interface ICertifierProvider
{
    /// <summary>The certifier and country this provider answers for. It is matched against the company's configuration.</summary>
    CertifierKey Key { get; }

    Task<CertificationResult> CertifyAsync(
        CertificationRequest request,
        CancellationToken cancellationToken = default);
}
