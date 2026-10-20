using WisFel.Worker.Api;

namespace WisFel.Worker.Certifiers;

/// <summary>
/// Selects a certifier provider: the certifier and the country, the two values the API assigns per company.
/// Both are normalized because they travel from the API catalog in whatever case it holds them, and a case
/// difference must not silently fail to resolve a provider.
/// </summary>
public sealed record CertifierKey(string CertifierCode, string CountryCode)
{
    public string CertifierCode { get; } = Normalize(CertifierCode, nameof(CertifierCode));

    public string CountryCode { get; } = Normalize(CountryCode, nameof(CountryCode));

    public static CertifierKey From(AgentCertifierConfigurationResponse configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new CertifierKey(configuration.CertifierCode, configuration.CountryCode);
    }

    public override string ToString() => $"{CertifierCode}/{CountryCode}";

    private static string Normalize(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);

        return value.Trim().ToUpperInvariant();
    }
}
