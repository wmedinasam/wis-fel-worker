using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WisFel.Worker.Api;
using WisFel.Worker.Data;

namespace WisFel.Worker.Tests;

public class AgentLicenseSynchronizerTests
{
    [Theory]
    [InlineData("Active", true)]
    [InlineData("Grace", true)]
    [InlineData("Suspended", false)]
    [InlineData("Whatever", false)]
    public void OnlyTheDocumentedActiveStatesAllowSending(string licenseState, bool allowed)
    {
        Assert.Equal(allowed, AgentLicenseSynchronizer.AllowsSending(licenseState));
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Grace", true)]
    [InlineData("Suspended", false)]
    public async Task TheCloudStateIsStoredOnTheCompany(string licenseState, bool licensed)
    {
        var databaseName = await SeedAsync(initiallyLicensed: !licensed);
        var client = new FakeFelApiClient
        {
            OnSession = _ => new FelApiSession(Guid.NewGuid(), Guid.NewGuid(), licenseState, null)
        };

        await CreateSynchronizer(databaseName, client).SynchronizeAsync();

        var company = await LoadCompanyAsync(databaseName);
        Assert.Equal(licensed, company.IsLicensed);
        Assert.NotNull(company.LicenseUpdatedAt);
    }

    [Fact]
    public async Task AForbiddenResponseDeniesSending()
    {
        var databaseName = await SeedAsync(initiallyLicensed: true);
        var client = new FakeFelApiClient
        {
            OnSession = _ => throw new FelApiLicenseException("agent/token/refresh", "1")
        };

        await CreateSynchronizer(databaseName, client).SynchronizeAsync();

        Assert.False((await LoadCompanyAsync(databaseName)).IsLicensed);
    }

    [Fact]
    public async Task ATransportFailureLeavesTheCompanyUntouched()
    {
        var databaseName = await SeedAsync(initiallyLicensed: true);
        var client = new FakeFelApiClient
        {
            OnSession = _ => throw new FelApiException(
                "agent/token/refresh",
                "1",
                HttpStatusCode.ServiceUnavailable,
                "unavailable")
        };

        await CreateSynchronizer(databaseName, client).SynchronizeAsync();

        Assert.True((await LoadCompanyAsync(databaseName)).IsLicensed);
    }

    [Fact]
    public async Task AMissingLicenseStateKeepsTheLastKnownValue()
    {
        var databaseName = await SeedAsync(initiallyLicensed: true);

        await CreateSynchronizer(databaseName, new FakeFelApiClient()).SynchronizeAsync();

        Assert.True((await LoadCompanyAsync(databaseName)).IsLicensed);
    }

    [Fact]
    public async Task ACompanyWithoutCredentialsIsNotSynchronized()
    {
        var databaseName = await SeedAsync(initiallyLicensed: false);
        var client = new FakeFelApiClient
        {
            OnSession = _ => new FelApiSession(null, null, "Active", null)
        };

        await CreateSynchronizer(databaseName, client, new FakeAgentCredentialStore("99")).SynchronizeAsync();

        Assert.False((await LoadCompanyAsync(databaseName)).IsLicensed);
    }

    private static AgentLicenseSynchronizer CreateSynchronizer(
        string databaseName,
        IFelApiClient client,
        IAgentCredentialStore? credentialStore = null) =>
        new(
            CreateContext(databaseName),
            client,
            credentialStore ?? new FakeAgentCredentialStore(),
            NullLogger<AgentLicenseSynchronizer>.Instance);

    private static async Task<string> SeedAsync(bool initiallyLicensed)
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var company = new SaeCompany(4.6m, 1);
        if (initiallyLicensed)
        {
            company.ApplyLicenseStatusFromCloud(true, DateTimeOffset.UtcNow);
        }

        context.SaeCompanies.Add(company);
        await context.SaveChangesAsync();

        // The fake store is configured for company key "1", which is the first identity value.
        Assert.Equal(1, company.Id);
        return databaseName;
    }

    private static async Task<SaeCompany> LoadCompanyAsync(string databaseName)
    {
        await using var context = CreateContext(databaseName);
        return await context.SaeCompanies.SingleAsync();
    }

    private static FelDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FelDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
