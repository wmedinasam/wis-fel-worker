using Microsoft.Data.SqlClient;
using WisFel.Worker.Data;

namespace WisFel.Worker.Tests;

public class SaeCompanyTests
{
    [Theory]
    [InlineData(4.0, 1, "SAE40Empre01")]
    [InlineData(4.6, 2, "SAE46Empre02")]
    [InlineData(5.0, 99, "SAE50Empre99")]
    [InlineData(10.0, 9, "SAE100Empre09")]
    public void DatabaseNameIsDerivedFromVersionAndCompany(decimal version, int number, string expected)
    {
        var company = new SaeCompany(version, number);

        Assert.Equal(expected, company.DatabaseName);
    }

    [Theory]
    [InlineData(3.9, 1)]
    [InlineData(4.61, 1)]
    [InlineData(4.0, 0)]
    [InlineData(4.0, 100)]
    public void InvalidCompanyConfigurationIsRejected(decimal version, int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaeCompany(version, number));
    }

    [Fact]
    public void CompanyIsUnlicensedUntilCloudStatusIsApplied()
    {
        var company = new SaeCompany(8.0m, 1);

        var decision = new DocumentSendPolicy().Evaluate(company);

        Assert.False(company.IsLicensed);
        Assert.Null(company.LicenseUpdatedAt);
        Assert.Equal(DocumentSendDenialReason.CompanyNotLicensed, decision.DenialReason);
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void UnconfiguredCompanyCannotSend()
    {
        var decision = new DocumentSendPolicy().Evaluate(null);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DocumentSendDenialReason.CompanyNotConfigured, decision.DenialReason);
    }

    [Fact]
    public void LicensedCompanyCanSend()
    {
        var company = new SaeCompany(8.0m, 1);
        company.ApplyLicenseStatusFromCloud(true, DateTimeOffset.UtcNow);

        var decision = new DocumentSendPolicy().Evaluate(company);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
    }

    [Fact]
    public void SaeConnectionUsesSharedServerAndCredentialsWithCompanyDatabase()
    {
        const string sharedConnectionString =
            "Server=localhost,1433;Database=FEL;Integrated Security=True;TrustServerCertificate=True";

        var company = new SaeCompany(4.6m, 1);
        var result = new SqlConnectionStringBuilder(
            SaeConnectionStringFactory.Create(sharedConnectionString, company));

        Assert.Equal("localhost,1433", result.DataSource);
        Assert.Equal("SAE46Empre01", result.InitialCatalog);
        Assert.True(result.IntegratedSecurity);
        Assert.True(result.TrustServerCertificate);
    }
}
