namespace WisFel.Worker.Api;

/// <summary>Brings the cloud license state into `dbo.SaeCompanies`. The API is the only authority.</summary>
public interface IAgentLicenseSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}
