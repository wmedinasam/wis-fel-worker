using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WisFel.Worker.Api;

internal sealed class AgentHandshakeRequest
{
    [JsonPropertyName("agentId")]
    public Guid AgentId { get; init; }

    [JsonPropertyName("secret")]
    public string Secret { get; init; } = string.Empty;

    [JsonPropertyName("workerVersion")]
    public string WorkerVersion { get; init; } = string.Empty;
}

internal sealed class AgentRefreshRequest
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; init; } = string.Empty;
}

internal sealed class AgentTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("licenseState")]
    public string? LicenseState { get; init; }

    [JsonPropertyName("handshakenAtUtc")]
    public DateTimeOffset? HandshakenAtUtc { get; init; }

    [JsonPropertyName("tenantId")]
    public Guid? TenantId { get; init; }

    [JsonPropertyName("companyId")]
    public Guid? CompanyId { get; init; }
}

internal sealed class AgentHeartbeatRequest
{
    [JsonPropertyName("workerVersion")]
    public string WorkerVersion { get; init; } = string.Empty;

    [JsonPropertyName("acknowledgedCommandIds")]
    public IReadOnlyList<Guid> AcknowledgedCommandIds { get; init; } = [];
}

internal sealed class DocumentStatusBatchRequest
{
    [JsonPropertyName("documents")]
    public IReadOnlyList<DocumentStatusReport> Documents { get; init; } = [];
}

/// <summary>Agent session data the API returns on handshake and refresh. Tokens stay inside the client.</summary>
public sealed record FelApiSession(
    Guid? TenantId,
    Guid? CompanyId,
    string? LicenseState,
    DateTimeOffset? HandshakenAtUtc);

public sealed class AgentHeartbeatResponse
{
    [JsonPropertyName("serverTimeUtc")]
    public DateTimeOffset ServerTimeUtc { get; init; }

    [JsonPropertyName("pendingCommands")]
    public IReadOnlyList<PendingAgentCommand> PendingCommands { get; init; } = [];
}

public sealed class PendingAgentCommand
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonObject? Payload { get; init; }

    [JsonPropertyName("leaseExpiresAtUtc")]
    public DateTimeOffset LeaseExpiresAtUtc { get; init; }
}

/// <summary>Document metadata reported to the API. It has no room for Request, Response or Pdf by design.</summary>
public sealed class DocumentStatusReport
{
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; init; } = string.Empty;

    [JsonPropertyName("branchId")]
    public Guid? BranchId { get; init; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = string.Empty;

    [JsonPropertyName("series")]
    public string? Series { get; init; }

    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("authorizationNumber")]
    public string? AuthorizationNumber { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("attemptCount")]
    public int AttemptCount { get; init; }

    [JsonPropertyName("issuedAtUtc")]
    public DateTimeOffset? IssuedAtUtc { get; init; }

    [JsonPropertyName("certifiedAtUtc")]
    public DateTimeOffset? CertifiedAtUtc { get; init; }
}

public sealed class DocumentStatusBatchResponse
{
    [JsonPropertyName("accepted")]
    public int Accepted { get; init; }

    [JsonPropertyName("rejected")]
    public IReadOnlyList<RejectedDocumentStatus> Rejected { get; init; } = [];
}

public sealed class RejectedDocumentStatus
{
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
