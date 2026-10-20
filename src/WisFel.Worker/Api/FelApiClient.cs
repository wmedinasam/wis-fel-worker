using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WisFel.Worker.Api;

public sealed class FelApiClient(
    IHttpClientFactory httpClientFactory,
    IAgentCredentialStore credentialStore,
    IOptions<FelApiOptions> options,
    ILogger<FelApiClient> logger) : IFelApiClient
{
    public const string HttpClientName = "fel-api";

    private const string HandshakeEndpoint = "agent/handshake";
    private const string RefreshEndpoint = "agent/token/refresh";
    private const string HeartbeatEndpoint = "agent/heartbeat";
    private const string DocumentStatusEndpoint = "agent/documents/status";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, AgentSession> sessions = new(StringComparer.Ordinal);

    public async Task<FelApiSession> HandshakeAsync(
        string companyKey,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession(companyKey);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            return await HandshakeCoreAsync(companyKey, session, cancellationToken);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task<FelApiSession> RefreshAsync(
        string companyKey,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession(companyKey);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            return await RefreshCoreAsync(companyKey, session, cancellationToken);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public Task<AgentHeartbeatResponse> HeartbeatAsync(
        string companyKey,
        IReadOnlyList<Guid> acknowledgedCommandIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledgedCommandIds);

        var request = new AgentHeartbeatRequest
        {
            WorkerVersion = options.Value.WorkerVersion,
            AcknowledgedCommandIds = acknowledgedCommandIds
        };

        return SendAuthenticatedAsync<AgentHeartbeatRequest, AgentHeartbeatResponse>(
            HeartbeatEndpoint,
            companyKey,
            request,
            cancellationToken);
    }

    public Task<DocumentStatusBatchResponse> ReportDocumentStatusAsync(
        string companyKey,
        IReadOnlyList<DocumentStatusReport> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count is < DocumentStatusReportMapper.MinBatchSize
            or > DocumentStatusReportMapper.MaxBatchSize)
        {
            throw new ArgumentException(
                $"A document status batch must hold between {DocumentStatusReportMapper.MinBatchSize} and " +
                $"{DocumentStatusReportMapper.MaxBatchSize} documents; it had {documents.Count}.",
                nameof(documents));
        }

        return SendAuthenticatedAsync<DocumentStatusBatchRequest, DocumentStatusBatchResponse>(
            DocumentStatusEndpoint,
            companyKey,
            new DocumentStatusBatchRequest { Documents = documents },
            cancellationToken);
    }

    private AgentSession GetSession(string companyKey)
    {
        // Throws when the company has no configured credential, before any session is created.
        credentialStore.GetCredentials(companyKey);

        return sessions.GetOrAdd(companyKey, key => new AgentSession(key, credentialStore));
    }

    private async Task<FelApiSession> HandshakeCoreAsync(
        string companyKey,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        var credentials = credentialStore.GetCredentials(companyKey);
        var response = await SendAsync<AgentHandshakeRequest, AgentTokenResponse>(
            HandshakeEndpoint,
            companyKey,
            new AgentHandshakeRequest
            {
                AgentId = credentials.AgentId,
                Secret = credentials.Secret,
                WorkerVersion = options.Value.WorkerVersion
            },
            accessToken: null,
            cancellationToken);

        return session.Store(response);
    }

    private async Task<FelApiSession> RefreshCoreAsync(
        string companyKey,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        if (session.RefreshToken is null)
        {
            return await HandshakeCoreAsync(companyKey, session, cancellationToken);
        }

        try
        {
            var response = await SendAsync<AgentRefreshRequest, AgentTokenResponse>(
                RefreshEndpoint,
                companyKey,
                new AgentRefreshRequest { RefreshToken = session.RefreshToken },
                accessToken: null,
                cancellationToken);

            return session.Store(response);
        }
        catch (FelApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The stored refresh token is gone or already rotated; the agent secret is still valid.
            logger.LogWarning(
                "The FEL API rejected the refresh token of company {CompanyKey}; falling back to handshake.",
                companyKey);
            session.Clear();
            return await HandshakeCoreAsync(companyKey, session, cancellationToken);
        }
    }

    private async Task<TResponse> SendAuthenticatedAsync<TRequest, TResponse>(
        string endpoint,
        string companyKey,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var session = GetSession(companyKey);
        var accessToken = await EnsureAccessTokenAsync(companyKey, session, cancellationToken);

        try
        {
            return await SendAsync<TRequest, TResponse>(
                endpoint,
                companyKey,
                request,
                accessToken,
                cancellationToken);
        }
        catch (FelApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            var renewedToken = await RenewAccessTokenAsync(
                companyKey,
                session,
                accessToken,
                cancellationToken);

            // One retry only: a second 401 with a fresh token is a real failure.
            return await SendAsync<TRequest, TResponse>(
                endpoint,
                companyKey,
                request,
                renewedToken,
                cancellationToken);
        }
    }

    private async Task<string> EnsureAccessTokenAsync(
        string companyKey,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        if (session.AccessToken is { } current)
        {
            return current;
        }

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (session.AccessToken is { } stored)
            {
                return stored;
            }

            // After a restart there is a persisted refresh token but no access token; RefreshCoreAsync
            // handshakes by itself when there is nothing to refresh.
            await RefreshCoreAsync(companyKey, session, cancellationToken);
            return session.AccessToken!;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task<string> RenewAccessTokenAsync(
        string companyKey,
        AgentSession session,
        string staleToken,
        CancellationToken cancellationToken)
    {
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (session.AccessToken is { } current && !string.Equals(current, staleToken, StringComparison.Ordinal))
            {
                return current;
            }

            await RefreshCoreAsync(companyKey, session, cancellationToken);
            return session.AccessToken!;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        string endpoint,
        string companyKey,
        TRequest request,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 1; ; attempt++)
        {
            HttpStatusCode? status = null;
            RetryConditionHeaderValue? retryAfter = null;
            Exception? transportFailure = null;

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(request, options: SerializerOptions)
                };

                if (accessToken is not null)
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }

                using var response = await client.SendAsync(message, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<TResponse>(
                               SerializerOptions,
                               cancellationToken)
                           ?? throw new FelApiException(
                               endpoint,
                               companyKey,
                               response.StatusCode,
                               $"The FEL API returned an empty body on {endpoint}.");
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new FelApiLicenseException(endpoint, companyKey);
                }

                status = response.StatusCode;
                retryAfter = response.Headers.RetryAfter;
            }
            catch (HttpRequestException exception)
            {
                transportFailure = exception;
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                transportFailure = exception;
            }

            var delay = attempt >= options.Value.MaxAttempts
                ? null
                : FelApiRetry.NextDelay(
                    status,
                    retryAfter,
                    attempt,
                    options.Value.RetryBaseDelay,
                    DateTimeOffset.UtcNow);

            if (delay is null)
            {
                if (transportFailure is not null)
                {
                    throw new FelApiException(
                        endpoint,
                        companyKey,
                        null,
                        $"The FEL API could not be reached on {endpoint} for company {companyKey}.",
                        transportFailure);
                }

                throw new FelApiException(
                    endpoint,
                    companyKey,
                    status,
                    $"The FEL API returned {(int)status!.Value} on {endpoint} for company {companyKey}.");
            }

            logger.LogWarning(
                "Retrying {Endpoint} for company {CompanyKey} after attempt {Attempt} ({Status}) in {Delay}.",
                endpoint,
                companyKey,
                attempt,
                status?.ToString() ?? "transport failure",
                delay.Value);
            await Task.Delay(delay.Value, cancellationToken);
        }
    }

    /// <summary>
    /// Per-company tokens. The refresh token stays in memory only: FEL must never store it and reusing a
    /// rotated one revokes the whole family, so a restart simply handshakes again with the agent secret.
    /// </summary>
    private sealed class AgentSession(string companyKey, IAgentCredentialStore credentialStore)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public string? AccessToken { get; private set; }

        public string? RefreshToken { get; private set; } = credentialStore.GetRefreshToken(companyKey);

        public FelApiSession Store(AgentTokenResponse response)
        {
            AccessToken = response.AccessToken;
            if (!string.IsNullOrEmpty(response.RefreshToken))
            {
                RefreshToken = response.RefreshToken;
                credentialStore.SaveRefreshToken(companyKey, response.RefreshToken);
            }

            return new FelApiSession(
                response.TenantId,
                response.CompanyId,
                response.LicenseState,
                response.HandshakenAtUtc);
        }

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
            credentialStore.ClearRefreshToken(companyKey);
        }
    }
}
