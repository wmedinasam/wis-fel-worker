namespace WisFel.Worker.Api;

/// <summary>
/// The four agent endpoints the FEL API exposes. <paramref name="companyKey"/> is the key under
/// FelApi:Agents, that is the FEL SaeCompanies.Id of the company whose agent credential is used.
/// </summary>
public interface IFelApiClient
{
    Task<FelApiSession> HandshakeAsync(string companyKey, CancellationToken cancellationToken = default);

    Task<FelApiSession> RefreshAsync(string companyKey, CancellationToken cancellationToken = default);

    Task<AgentHeartbeatResponse> HeartbeatAsync(
        string companyKey,
        IReadOnlyList<Guid> acknowledgedCommandIds,
        CancellationToken cancellationToken = default);

    Task<DocumentStatusBatchResponse> ReportDocumentStatusAsync(
        string companyKey,
        IReadOnlyList<DocumentStatusReport> documents,
        CancellationToken cancellationToken = default);
}
