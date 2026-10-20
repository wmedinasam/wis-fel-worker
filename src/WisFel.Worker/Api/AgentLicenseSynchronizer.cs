using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WisFel.Worker.Data;

namespace WisFel.Worker.Api;

public sealed class AgentLicenseSynchronizer(
    FelDbContext felDbContext,
    IFelApiClient felApiClient,
    IAgentCredentialStore credentialStore,
    CertifierConfigurationCache certifierConfigurations,
    ILogger<AgentLicenseSynchronizer> logger) : IAgentLicenseSynchronizer
{
    /// <summary>Deny by default: only the states the API documents as valid allow sending.</summary>
    public static bool AllowsSending(string licenseState) =>
        licenseState is "Active" or "Grace";

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var companies = await felDbContext.SaeCompanies.ToListAsync(cancellationToken);

        foreach (var company in companies)
        {
            var companyKey = company.Id.ToString(CultureInfo.InvariantCulture);
            if (!credentialStore.IsConfigured(companyKey))
            {
                logger.LogDebug(
                    "Company {CompanyKey} has no agent credential configured; its license is not synchronized.",
                    companyKey);
                continue;
            }

            try
            {
                var session = await felApiClient.RefreshAsync(companyKey, cancellationToken);
                if (session.LicenseState is null)
                {
                    logger.LogDebug(
                        "The API returned no license state for company {CompanyKey}; it keeps its last value.",
                        companyKey);
                    continue;
                }

                var allowsSending = AllowsSending(session.LicenseState);
                company.ApplyLicenseStatusFromCloud(allowsSending, DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Company {CompanyKey} reports license state {LicenseState}.",
                    companyKey,
                    session.LicenseState);

                if (allowsSending)
                {
                    await CacheCertifierConfigurationAsync(companyKey, cancellationToken);
                }
                else
                {
                    certifierConfigurations.Remove(companyKey);
                }
            }
            catch (FelApiLicenseException)
            {
                company.ApplyLicenseStatusFromCloud(false, DateTimeOffset.UtcNow);
                certifierConfigurations.Remove(companyKey);
                logger.LogWarning("The API denied the license of company {CompanyKey}.", companyKey);
            }
            catch (FelApiException exception)
            {
                // Without a synchronization the company keeps whatever it had; the worker never guesses.
                logger.LogWarning(
                    "The license of company {CompanyKey} could not be synchronized ({StatusCode}).",
                    companyKey,
                    exception.StatusCode);
            }
        }

        await felDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CacheCertifierConfigurationAsync(string companyKey, CancellationToken cancellationToken)
    {
        try
        {
            certifierConfigurations.Store(
                companyKey,
                await felApiClient.GetCertifierConfigurationAsync(companyKey, cancellationToken));
        }
        catch (FelApiException exception) when (exception is not FelApiLicenseException)
        {
            // A company without a certifier assignment (404/409) is not a license failure: the next pass retries.
            certifierConfigurations.Remove(companyKey);
            logger.LogWarning(
                "The certifier configuration of company {CompanyKey} could not be fetched ({StatusCode}).",
                companyKey,
                exception.StatusCode);
        }
    }
}
