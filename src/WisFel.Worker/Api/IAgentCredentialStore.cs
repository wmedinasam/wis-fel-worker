namespace WisFel.Worker.Api;

/// <summary>
/// The only place the worker reads agent secrets from and the only place the rotating refresh token is kept.
/// The agent secret always comes from configuration (User Secrets, environment or Docker secrets); the
/// refresh token is persisted encrypted outside FEL so a restart does not need a new handshake.
/// </summary>
public interface IAgentCredentialStore
{
    FelApiAgentCredentials GetCredentials(string companyKey);

    string? GetRefreshToken(string companyKey);

    void SaveRefreshToken(string companyKey, string refreshToken);

    void ClearRefreshToken(string companyKey);
}
