using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace WisFel.Worker.Api;

public sealed class ProtectedAgentCredentialStore : IAgentCredentialStore
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly IOptions<FelApiOptions> options;
    private readonly IDataProtector protector;
    private readonly ILogger<ProtectedAgentCredentialStore> logger;

    public ProtectedAgentCredentialStore(
        IOptions<FelApiOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ProtectedAgentCredentialStore> logger)
    {
        this.options = options;
        this.logger = logger;
        protector = dataProtectionProvider.CreateProtector("WisFel.Worker.AgentRefreshToken");
    }

    public bool IsConfigured(string companyKey) =>
        !string.IsNullOrWhiteSpace(companyKey) && options.Value.Agents.ContainsKey(companyKey);

    public FelApiAgentCredentials GetCredentials(string companyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyKey);

        if (!options.Value.Agents.TryGetValue(companyKey, out var credentials))
        {
            throw new InvalidOperationException(
                $"No agent credential is configured under FelApi:Agents:{companyKey}.");
        }

        return credentials;
    }

    public string? GetRefreshToken(string companyKey)
    {
        var path = GetTokenFilePath(companyKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Lost key ring or a file from another installation: drop it and let the caller handshake again.
            logger.LogWarning(
                "The stored refresh token of company {CompanyKey} could not be read and was discarded.",
                companyKey);
            TryDelete(path, companyKey);
            return null;
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "The refresh token of company {CompanyKey} could not be read from the token store.",
                companyKey);
            return null;
        }
    }

    public void SaveRefreshToken(string companyKey, string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var path = GetTokenFilePath(companyKey);
        var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";

        try
        {
            CreateTokenDirectory(path);
            File.WriteAllText(temporaryPath, protector.Protect(refreshToken));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, OwnerOnlyFile);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing durability must not break a heartbeat: the in-memory token still works.
            logger.LogWarning(
                exception,
                "The refresh token of company {CompanyKey} could not be persisted to {TokenStorePath}.",
                companyKey,
                options.Value.TokenStorePath);
            TryDelete(temporaryPath, companyKey);
        }
    }

    public void ClearRefreshToken(string companyKey) => TryDelete(GetTokenFilePath(companyKey), companyKey);

    private string GetTokenFilePath(string companyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyKey);

        // The key reaches a file name, so anything but a plain identifier is rejected instead of sanitized.
        if (!companyKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException(
                "A company key may only contain letters, digits, '-' and '_'.",
                nameof(companyKey));
        }

        return Path.Combine(options.Value.TokenStorePath, "tokens", $"agent-{companyKey}.token");
    }

    private static void CreateTokenDirectory(string tokenFilePath)
    {
        var directory = Path.GetDirectoryName(tokenFilePath)!;
        if (Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnlyDirectory);
        }
    }

    private void TryDelete(string path, string companyKey)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "The stored refresh token of company {CompanyKey} could not be deleted.",
                companyKey);
        }
    }
}
