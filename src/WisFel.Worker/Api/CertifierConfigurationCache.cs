using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace WisFel.Worker.Api;

/// <summary>
/// Last certifier configuration per company. It lives in memory only: the credentials are never written to FEL,
/// the repository or the image, and a restart fetches them again from the API.
/// </summary>
public sealed class CertifierConfigurationCache
{
    private readonly ConcurrentDictionary<string, AgentCertifierConfigurationResponse> configurations =
        new(StringComparer.Ordinal);

    public void Store(string companyKey, AgentCertifierConfigurationResponse configuration) =>
        configurations[companyKey] = configuration;

    public bool TryGet(string companyKey, [NotNullWhen(true)] out AgentCertifierConfigurationResponse? configuration) =>
        configurations.TryGetValue(companyKey, out configuration);

    /// <summary>Drops the credentials of a company whose license no longer allows sending.</summary>
    public void Remove(string companyKey) => configurations.TryRemove(companyKey, out _);
}
