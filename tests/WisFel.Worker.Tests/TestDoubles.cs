using System.Reflection;
using WisFel.Worker.Api;
using WisFel.Worker.Data;

namespace WisFel.Worker.Tests;

/// <summary>The SAE trigger inserts the queue rows, so <see cref="Document"/> has no public constructor.</summary>
internal static class TestDocuments
{
    public static Document Create(
        int companyId = 7,
        int locationId = 3,
        int internalId = 4521,
        string documentType = "FACT",
        DocumentStatus status = DocumentStatus.Pending,
        string? uuid = null,
        string? serial = null,
        string? number = null,
        string? observations = null,
        string? request = null)
    {
        var document = (Document)Activator.CreateInstance(typeof(Document), nonPublic: true)!;

        Set(document, nameof(Document.CompanyId), companyId);
        Set(document, nameof(Document.LocationId), locationId);
        Set(document, nameof(Document.InternalId), internalId);
        Set(document, nameof(Document.DocumentType), documentType);
        Set(document, nameof(Document.InternalNumber), internalId.ToString());
        Set(document, nameof(Document.InternalSerial), "F");
        Set(document, nameof(Document.Status), status);
        Set(document, nameof(Document.Uuid), uuid);
        Set(document, nameof(Document.Serial), serial);
        Set(document, nameof(Document.Number), number);
        Set(document, nameof(Document.Observations), observations);
        Set(document, nameof(Document.Request), request);

        return document;
    }

    private static void Set(Document document, string propertyName, object? value) =>
        typeof(Document)
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(document, value);
}

internal sealed class FakeAgentCredentialStore(params string[] configuredKeys) : IAgentCredentialStore
{
    public static readonly Guid DefaultAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly HashSet<string> configured = configuredKeys.Length == 0
        ? new HashSet<string>(StringComparer.Ordinal) { "1" }
        : new HashSet<string>(configuredKeys, StringComparer.Ordinal);

    private readonly Dictionary<string, string> refreshTokens = new(StringComparer.Ordinal);

    public bool IsConfigured(string companyKey) => configured.Contains(companyKey);

    public FelApiAgentCredentials GetCredentials(string companyKey) => configured.Contains(companyKey)
        ? new FelApiAgentCredentials { AgentId = DefaultAgentId, Secret = "agent-secret" }
        : throw new InvalidOperationException($"No agent credential for {companyKey}.");

    public string? GetRefreshToken(string companyKey) =>
        refreshTokens.TryGetValue(companyKey, out var token) ? token : null;

    public void SaveRefreshToken(string companyKey, string refreshToken) =>
        refreshTokens[companyKey] = refreshToken;

    public void ClearRefreshToken(string companyKey) => refreshTokens.Remove(companyKey);
}

internal sealed class FakeFelApiClient : IFelApiClient
{
    /// <summary>Answers (or throws) for handshake and refresh; the default keeps the license unknown.</summary>
    public Func<string, FelApiSession> OnSession { get; set; } =
        _ => new FelApiSession(null, null, null, null);

    public Exception? ReportFailure { get; set; }

    public List<(string CompanyKey, IReadOnlyList<DocumentStatusReport> Reports)> Reports { get; } = [];

    public List<string> Heartbeats { get; } = [];

    public DocumentStatusBatchResponse ReportResponse { get; set; } = new();

    public Task<FelApiSession> HandshakeAsync(string companyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(OnSession(companyKey));

    public Task<FelApiSession> RefreshAsync(string companyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(OnSession(companyKey));

    public Task<AgentHeartbeatResponse> HeartbeatAsync(
        string companyKey,
        IReadOnlyList<Guid> acknowledgedCommandIds,
        CancellationToken cancellationToken = default)
    {
        Heartbeats.Add(companyKey);
        return Task.FromResult(new AgentHeartbeatResponse());
    }

    public Task<DocumentStatusBatchResponse> ReportDocumentStatusAsync(
        string companyKey,
        IReadOnlyList<DocumentStatusReport> documents,
        CancellationToken cancellationToken = default)
    {
        Reports.Add((companyKey, documents));
        return ReportFailure is null
            ? Task.FromResult(ReportResponse)
            : Task.FromException<DocumentStatusBatchResponse>(ReportFailure);
    }
}
