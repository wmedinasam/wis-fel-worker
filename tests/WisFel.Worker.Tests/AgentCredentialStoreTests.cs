using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WisFel.Worker.Api;

namespace WisFel.Worker.Tests;

public sealed class AgentCredentialStoreTests : IDisposable
{
    private const string CompanyKey = "1";

    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(),
        $"wis-fel-worker-tests-{Guid.NewGuid():N}");

    [Fact]
    public void RefreshTokenSurvivesAsCiphertextOnDisk()
    {
        var store = CreateStore();

        store.SaveRefreshToken(CompanyKey, "refresh-secret-value");

        Assert.Equal("refresh-secret-value", store.GetRefreshToken(CompanyKey));
        var tokenFile = Assert.Single(Directory.GetFiles(Path.Combine(storeRoot, "tokens")));
        Assert.DoesNotContain("refresh-secret-value", File.ReadAllText(tokenFile), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTokenFileIsReadableOnlyByItsOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = CreateStore();

        store.SaveRefreshToken(CompanyKey, "refresh-secret-value");

        var tokenFile = Assert.Single(Directory.GetFiles(Path.Combine(storeRoot, "tokens")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tokenFile));
    }

    [Fact]
    public void SavingAgainRotatesTheStoredToken()
    {
        var store = CreateStore();

        store.SaveRefreshToken(CompanyKey, "refresh-1");
        store.SaveRefreshToken(CompanyKey, "refresh-2");

        Assert.Equal("refresh-2", store.GetRefreshToken(CompanyKey));
        Assert.Single(Directory.GetFiles(Path.Combine(storeRoot, "tokens")));
    }

    [Fact]
    public void AnUnreadableTokenIsDiscardedInsteadOfThrowing()
    {
        CreateStore().SaveRefreshToken(CompanyKey, "refresh-1");
        var tokenFile = Assert.Single(Directory.GetFiles(Path.Combine(storeRoot, "tokens")));

        // A file from another installation: a different key ring cannot unprotect it.
        var otherInstallation = CreateStore();

        Assert.Null(otherInstallation.GetRefreshToken(CompanyKey));
        Assert.False(File.Exists(tokenFile));
    }

    [Fact]
    public void AMissingOrClearedTokenReadsAsNull()
    {
        var store = CreateStore();

        Assert.Null(store.GetRefreshToken(CompanyKey));

        store.SaveRefreshToken(CompanyKey, "refresh-1");
        store.ClearRefreshToken(CompanyKey);

        Assert.Null(store.GetRefreshToken(CompanyKey));
    }

    [Fact]
    public void ClearingAnAbsentTokenIsHarmless()
    {
        CreateStore().ClearRefreshToken(CompanyKey);
    }

    [Fact]
    public void AnUnconfiguredCompanyHasNoCredentials()
    {
        var store = CreateStore();

        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            store.GetCredentials(CompanyKey).AgentId);
        Assert.Throws<InvalidOperationException>(() => store.GetCredentials("99"));
    }

    [Fact]
    public void ACompanyKeyCannotEscapeTheTokenDirectory()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.GetRefreshToken("../../etc/passwd"));
        Assert.Throws<ArgumentException>(() => store.SaveRefreshToken("1/../2", "refresh-1"));
    }

    public void Dispose()
    {
        if (Directory.Exists(storeRoot))
        {
            Directory.Delete(storeRoot, recursive: true);
        }
    }

    /// <summary>Each store gets its own ephemeral key ring, like two separate installations.</summary>
    private ProtectedAgentCredentialStore CreateStore()
    {
        var options = new FelApiOptions
        {
            BaseUrl = "https://fel-api.test",
            TokenStorePath = storeRoot,
            Agents =
            {
                ["1"] = new FelApiAgentCredentials
                {
                    AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Secret = "agent-secret"
                }
            }
        };

        return new ProtectedAgentCredentialStore(
            Options.Create(options),
            new EphemeralDataProtectionProvider(),
            NullLogger<ProtectedAgentCredentialStore>.Instance);
    }
}
