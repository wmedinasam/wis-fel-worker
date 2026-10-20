namespace WisFel.Worker.Certifiers;

public interface ICertifierProviderFactory
{
    /// <summary>Throws when no provider covers the key, naming the ones that are registered.</summary>
    ICertifierProvider Resolve(CertifierKey key);
}

/// <summary>
/// Picks the provider for a company out of the registered ones. Each provider declares its own key, so
/// adding a certifier or a country is one registration and no key kept in sync by hand.
/// </summary>
public sealed class CertifierProviderFactory : ICertifierProviderFactory
{
    private readonly IReadOnlyDictionary<CertifierKey, ICertifierProvider> providers;

    public CertifierProviderFactory(IEnumerable<ICertifierProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        // Two providers on the same key is a registration bug: fail at startup, not on the first document.
        this.providers = providers.ToDictionary(provider => provider.Key);
    }

    public ICertifierProvider Resolve(CertifierKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (providers.TryGetValue(key, out var provider))
        {
            return provider;
        }

        var available = providers.Count == 0
            ? "none"
            : string.Join(", ", providers.Keys.Select(registered => registered.ToString()).Order(StringComparer.Ordinal));

        throw new InvalidOperationException(
            $"No certifier provider is registered for {key}. Registered providers: {available}.");
    }
}
