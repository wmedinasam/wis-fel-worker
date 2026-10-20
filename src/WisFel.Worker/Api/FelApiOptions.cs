using Microsoft.Extensions.Options;

namespace WisFel.Worker.Api;

public sealed class FelApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string WorkerVersion { get; set; } = "1.0.0";

    public int MaxAttempts { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Agent credentials keyed by the FEL SaeCompanies.Id of the company they belong to.</summary>
    public Dictionary<string, FelApiAgentCredentials> Agents { get; set; } = [];

    public bool IsValid(out string error)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri))
        {
            error = "FelApi:BaseUrl must be an absolute URL.";
            return false;
        }

        // Agent credentials travel in the body: plain HTTP is only tolerated against a local API.
        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
        {
            error = "FelApi:BaseUrl must use HTTPS unless it points to localhost.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WorkerVersion))
        {
            error = "FelApi:WorkerVersion is required.";
            return false;
        }

        if (MaxAttempts < 1)
        {
            error = "FelApi:MaxAttempts must be at least 1.";
            return false;
        }

        foreach (var (companyKey, credentials) in Agents)
        {
            if (credentials.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(credentials.Secret))
            {
                error = $"FelApi:Agents:{companyKey} needs both AgentId and Secret.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}

public sealed class FelApiAgentCredentials
{
    public Guid AgentId { get; set; }

    public string Secret { get; set; } = string.Empty;
}

internal sealed class FelApiOptionsValidator : IValidateOptions<FelApiOptions>
{
    public ValidateOptionsResult Validate(string? name, FelApiOptions options) =>
        options.IsValid(out var error) ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
}
