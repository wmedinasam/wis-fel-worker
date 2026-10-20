using WisFel.Worker.Api;
using WisFel.Worker.Certifiers;

namespace WisFel.Worker.Tests;

public class CertifierProviderFactoryTests
{
    [Fact]
    public void ResolvesTheProviderWhoseKeyMatches()
    {
        var infileGt = new FakeCertifierProvider(new CertifierKey("INFILE", "GT"));
        var infileSv = new FakeCertifierProvider(new CertifierKey("INFILE", "SV"));
        var factory = new CertifierProviderFactory([infileGt, infileSv]);

        Assert.Same(infileSv, factory.Resolve(new CertifierKey("INFILE", "SV")));
    }

    [Fact]
    public void ResolutionIgnoresCaseAndSurroundingSpaces()
    {
        var infileGt = new FakeCertifierProvider(new CertifierKey("INFILE", "GT"));
        var factory = new CertifierProviderFactory([infileGt]);

        Assert.Same(infileGt, factory.Resolve(new CertifierKey(" infile ", "gt")));
    }

    [Fact]
    public void AnUnknownKeyNamesTheRegisteredProviders()
    {
        var factory = new CertifierProviderFactory([new FakeCertifierProvider(new CertifierKey("INFILE", "GT"))]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.Resolve(new CertifierKey("FEL", "RD")));

        Assert.Contains("FEL/RD", exception.Message);
        Assert.Contains("INFILE/GT", exception.Message);
    }

    [Fact]
    public void AnEmptyFactoryReportsThatNothingIsRegistered()
    {
        var factory = new CertifierProviderFactory([]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.Resolve(new CertifierKey("INFILE", "GT")));

        Assert.Contains("Registered providers: none", exception.Message);
    }

    [Fact]
    public void TwoProvidersOnTheSameKeyFailAtConstruction()
    {
        var providers = new[]
        {
            new FakeCertifierProvider(new CertifierKey("INFILE", "GT")),
            new FakeCertifierProvider(new CertifierKey(" infile ", "gt"))
        };

        Assert.Throws<ArgumentException>(() => new CertifierProviderFactory(providers));
    }

    [Fact]
    public void KeyIsDerivedFromTheCompanyConfiguration()
    {
        var configuration = new AgentCertifierConfigurationResponse
        {
            CertifierCode = "infile",
            CountryCode = "gt"
        };

        Assert.Equal(new CertifierKey("INFILE", "GT"), CertifierKey.From(configuration));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCodeIsRejected(string code)
    {
        Assert.Throws<ArgumentException>(() => new CertifierKey(code, "GT"));
        Assert.Throws<ArgumentException>(() => new CertifierKey("INFILE", code));
    }

    private sealed class FakeCertifierProvider(CertifierKey key) : ICertifierProvider
    {
        public CertifierKey Key { get; } = key;

        public Task<CertificationResult> CertifyAsync(
            CertificationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CertificationResult.Certified("00000000-0000-0000-0000-000000000000"));
    }
}
