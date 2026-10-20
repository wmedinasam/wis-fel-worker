using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WisFel.Worker.Api;

namespace WisFel.Worker.Tests;

public class FelApiClientTests
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string CompanyKey = "1";

    private const string HandshakeJson = """
        {
          "accessToken": "access-1",
          "refreshToken": "refresh-1",
          "licenseState": "Grace",
          "handshakenAtUtc": "2026-10-20T10:00:00Z",
          "tenantId": "22222222-2222-2222-2222-222222222222",
          "companyId": "33333333-3333-3333-3333-333333333333"
        }
        """;

    private const string HeartbeatJson = """
        {
          "serverTimeUtc": "2026-10-20T10:00:05Z",
          "pendingCommands": [
            {
              "id": "44444444-4444-4444-4444-444444444444",
              "type": "ResendDocument",
              "payload": { "documentId": "55555555-5555-5555-5555-555555555555", "idempotencyKey": "1-2-FACT-7" },
              "leaseExpiresAtUtc": "2026-10-20T10:00:35Z"
            }
          ]
        }
        """;

    [Fact]
    public async Task HandshakeSendsCredentialsAndExposesLicenseState()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        var client = CreateClient(handler);

        var session = await client.HandshakeAsync(CompanyKey);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/agent/handshake", request.Path);
        Assert.Null(request.AccessToken);
        Assert.Contains(AgentId.ToString(), request.Body);
        Assert.Contains("1.2.3", request.Body);
        Assert.Equal("Grace", session.LicenseState);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), session.TenantId);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), session.CompanyId);
    }

    [Fact]
    public async Task HeartbeatHandshakesOnceAndReusesTheAccessToken()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        handler.Enqueue(HttpStatusCode.OK, HeartbeatJson);
        handler.Enqueue(HttpStatusCode.OK, HeartbeatJson);
        var client = CreateClient(handler);

        var first = await client.HeartbeatAsync(CompanyKey, []);
        await client.HeartbeatAsync(
            CompanyKey,
            [Guid.Parse("44444444-4444-4444-4444-444444444444")]);

        var command = Assert.Single(first.PendingCommands);
        Assert.Equal("ResendDocument", command.Type);
        Assert.Equal(
            "55555555-5555-5555-5555-555555555555",
            command.Payload!["documentId"]!.GetValue<string>());
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/agent/handshake", handler.Requests[0].Path);
        Assert.Equal("/agent/heartbeat", handler.Requests[1].Path);
        Assert.Equal("access-1", handler.Requests[1].AccessToken);
        Assert.Equal("access-1", handler.Requests[2].AccessToken);
        Assert.Contains("44444444-4444-4444-4444-444444444444", handler.Requests[2].Body);
    }

    [Fact]
    public async Task UnauthorizedHeartbeatRefreshesTheTokenAndRetriesOnce()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpStatusCode.OK, """{"accessToken":"access-2","refreshToken":"refresh-2","licenseState":"Active"}""");
        handler.Enqueue(HttpStatusCode.OK, HeartbeatJson);
        var client = CreateClient(handler);

        await client.HeartbeatAsync(CompanyKey, []);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("/agent/token/refresh", handler.Requests[2].Path);
        Assert.Contains("refresh-1", handler.Requests[2].Body);
        Assert.Equal("/agent/heartbeat", handler.Requests[3].Path);
        Assert.Equal("access-2", handler.Requests[3].AccessToken);
    }

    [Fact]
    public async Task ASecondUnauthorizedResponseIsNotRetriedAgain()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpStatusCode.OK, """{"accessToken":"access-2","refreshToken":"refresh-2"}""");
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FelApiException>(
            () => client.HeartbeatAsync(CompanyKey, []));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task ForbiddenMeansInvalidLicense()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "{}");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FelApiLicenseException>(
            () => client.HandshakeAsync(CompanyKey));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TooManyRequestsIsRetriedUntilMaxAttempts()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 0);
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 0);
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 0);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FelApiException>(
            () => client.HandshakeAsync(CompanyKey));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorIsRetriedAndThenSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadGateway, "{}");
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        var client = CreateClient(handler);

        var session = await client.HandshakeAsync(CompanyKey);

        Assert.Equal("Grace", session.LicenseState);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task BadRequestIsNotRetried()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, "{}");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<FelApiException>(
            () => client.HandshakeAsync(CompanyKey));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DocumentStatusBatchKeepsTheApiLimits()
    {
        var client = CreateClient(new StubHttpMessageHandler());
        var report = new DocumentStatusReport { IdempotencyKey = "1-2-FACT-7", Status = "Certified" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ReportDocumentStatusAsync(CompanyKey, []));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ReportDocumentStatusAsync(
                CompanyKey,
                Enumerable.Repeat(report, 101).ToArray()));
    }

    [Fact]
    public async Task DocumentStatusBatchIsPostedWithTheAgentToken()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"accepted":1,"rejected":[{"idempotencyKey":"1-2-FACT-8","reason":"Unknown branch"}]}""");
        var client = CreateClient(handler);

        var response = await client.ReportDocumentStatusAsync(
            CompanyKey,
            [new DocumentStatusReport { IdempotencyKey = "1-2-FACT-7", Status = "Certified" }]);

        Assert.Equal(1, response.Accepted);
        Assert.Equal("1-2-FACT-8", Assert.Single(response.Rejected).IdempotencyKey);
        Assert.Equal("/agent/documents/status", handler.Requests[1].Path);
        Assert.Equal("access-1", handler.Requests[1].AccessToken);
        Assert.Contains("1-2-FACT-7", handler.Requests[1].Body);
    }

    [Fact]
    public async Task AnUnconfiguredCompanyCannotCallTheApi()
    {
        var client = CreateClient(new StubHttpMessageHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.HandshakeAsync("99"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void OnlyTransientStatusesAreRetried(HttpStatusCode status, bool retried)
    {
        var delay = FelApiRetry.NextDelay(
            status,
            retryAfter: null,
            attempt: 1,
            TimeSpan.FromSeconds(1),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(retried, delay is not null);
    }

    [Fact]
    public void RetryAfterOverridesTheExponentialBackoff()
    {
        var now = DateTimeOffset.UnixEpoch;

        var fromDelta = FelApiRetry.NextDelay(
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)),
            attempt: 3,
            TimeSpan.FromSeconds(1),
            now);
        var fromDate = FelApiRetry.NextDelay(
            HttpStatusCode.ServiceUnavailable,
            new RetryConditionHeaderValue(now.AddSeconds(5)),
            attempt: 1,
            TimeSpan.FromSeconds(1),
            now);
        var fromPastDate = FelApiRetry.NextDelay(
            HttpStatusCode.ServiceUnavailable,
            new RetryConditionHeaderValue(now.AddSeconds(-5)),
            attempt: 1,
            TimeSpan.FromSeconds(1),
            now);

        Assert.Equal(TimeSpan.FromSeconds(2), fromDelta);
        Assert.Equal(TimeSpan.FromSeconds(5), fromDate);
        Assert.Equal(TimeSpan.Zero, fromPastDate);
    }

    [Fact]
    public void TransportFailuresAreRetriedWithExponentialBackoff()
    {
        var first = FelApiRetry.NextDelay(
            status: null,
            retryAfter: null,
            attempt: 1,
            TimeSpan.FromSeconds(1),
            DateTimeOffset.UnixEpoch);
        var second = FelApiRetry.NextDelay(
            status: null,
            retryAfter: null,
            attempt: 2,
            TimeSpan.FromSeconds(1),
            DateTimeOffset.UnixEpoch);

        Assert.InRange(first!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.25));
        Assert.InRange(second!.Value, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public async Task APersistedRefreshTokenReplacesTheHandshakeAfterARestart()
    {
        var store = CreateStore("refresh-saved");
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"accessToken":"access-9","refreshToken":"refresh-10"}""");
        handler.Enqueue(HttpStatusCode.OK, HeartbeatJson);
        var client = CreateClient(handler, store);

        await client.HeartbeatAsync(CompanyKey, []);

        Assert.Equal("/agent/token/refresh", handler.Requests[0].Path);
        Assert.Contains("refresh-saved", handler.Requests[0].Body);
        Assert.Equal("refresh-10", store.GetRefreshToken(CompanyKey));
        Assert.Equal("access-9", handler.Requests[1].AccessToken);
    }

    [Fact]
    public async Task ARejectedRefreshTokenIsDiscardedFromTheStore()
    {
        var store = CreateStore("refresh-stale");
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        handler.Enqueue(HttpStatusCode.OK, HeartbeatJson);
        var client = CreateClient(handler, store);

        await client.HeartbeatAsync(CompanyKey, []);

        Assert.Equal("/agent/token/refresh", handler.Requests[0].Path);
        Assert.Equal("/agent/handshake", handler.Requests[1].Path);
        Assert.Equal("refresh-1", store.GetRefreshToken(CompanyKey));
    }

    [Fact]
    public async Task TheRefreshTokenIsPersistedOnHandshake()
    {
        var store = new FakeAgentCredentialStore();
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, HandshakeJson);
        var client = CreateClient(handler, store);

        await client.HandshakeAsync(CompanyKey);

        Assert.Equal("refresh-1", store.GetRefreshToken(CompanyKey));
    }

    private static FakeAgentCredentialStore CreateStore(string refreshToken)
    {
        var store = new FakeAgentCredentialStore();
        store.SaveRefreshToken(CompanyKey, refreshToken);
        return store;
    }

    private static FelApiClient CreateClient(
        StubHttpMessageHandler handler,
        FakeAgentCredentialStore? store = null)
    {
        var options = new FelApiOptions
        {
            BaseUrl = "https://fel-api.test",
            WorkerVersion = "1.2.3",
            MaxAttempts = 3,
            RetryBaseDelay = TimeSpan.Zero,
            Agents =
            {
                [CompanyKey] = new FelApiAgentCredentials { AgentId = AgentId, Secret = "agent-secret" }
            }
        };

        Assert.True(options.IsValid(out _));

        return new FelApiClient(
            new StubHttpClientFactory(handler, new Uri($"{options.BaseUrl}/")),
            store ?? new FakeAgentCredentialStore(),
            Options.Create(options),
            NullLogger<FelApiClient>.Instance);
    }

    private sealed record RecordedRequest(string Path, string? AccessToken, string Body);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string json, int? retryAfterSeconds = null)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Parameter,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            Assert.True(responses.Count > 0, $"No stub response left for {request.RequestUri}.");
            return responses.Dequeue();
        }
    }
}
