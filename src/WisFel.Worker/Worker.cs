using Microsoft.Extensions.Options;
using WisFel.Worker.Api;
using WisFel.Worker.Queue;

namespace WisFel.Worker;

/// <summary>
/// One sequential loop, so no pass overlaps another: license synchronization, queue consumption and the
/// heartbeat all run on the poll tick, each one when its own interval is due.
/// </summary>
public sealed class Worker(
    IServiceProvider serviceProvider,
    IFelApiClient felApiClient,
    IOptions<WorkerOptions> workerOptions,
    IOptions<FelApiOptions> felApiOptions,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FelApiOptions api;
        try
        {
            api = felApiOptions.Value;
        }
        catch (OptionsValidationException exception)
        {
            // Without the API there is no license, and without a license nothing may be sent: stay idle
            // instead of failing the same way every few seconds.
            logger.LogWarning(
                "The FEL API is not configured, so the queue is not consumed: {Reason}",
                string.Join(" ", exception.Failures));
            return;
        }

        var options = workerOptions.Value;
        var lastLicenseSync = DateTimeOffset.MinValue;
        var lastHeartbeat = DateTimeOffset.MinValue;

        logger.LogInformation(
            "Worker started: polling the queue every {PollInterval}, heartbeat every {HeartbeatInterval}.",
            options.PollInterval,
            options.HeartbeatInterval);

        using var timer = new PeriodicTimer(options.PollInterval);
        try
        {
            do
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    await using var scope = serviceProvider.CreateAsyncScope();

                    if (now - lastLicenseSync >= options.LicenseSyncInterval)
                    {
                        await scope.ServiceProvider
                            .GetRequiredService<IAgentLicenseSynchronizer>()
                            .SynchronizeAsync(stoppingToken);
                        lastLicenseSync = now;
                    }

                    var prepared = await scope.ServiceProvider
                        .GetRequiredService<IDocumentQueueProcessor>()
                        .ProcessPendingAsync(stoppingToken);
                    if (prepared > 0)
                    {
                        logger.LogInformation("Prepared the request JSON of {Count} queued documents.", prepared);
                    }

                    if (now - lastHeartbeat >= options.HeartbeatInterval)
                    {
                        await SendHeartbeatsAsync(api, stoppingToken);
                        lastHeartbeat = now;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A failing SAE query or API call must not take the worker down.
                    logger.LogError(exception, "The worker pass failed; it runs again on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Worker stopped.");
    }

    private async Task SendHeartbeatsAsync(FelApiOptions api, CancellationToken cancellationToken)
    {
        foreach (var companyKey in api.Agents.Keys)
        {
            try
            {
                var heartbeat = await felApiClient.HeartbeatAsync(companyKey, [], cancellationToken);
                foreach (var command in heartbeat.PendingCommands)
                {
                    // Not acknowledged on purpose: nothing attends commands yet, so the API has to redeliver
                    // them once the lease expires instead of considering them done.
                    logger.LogInformation(
                        "Company {CompanyKey} received the command {CommandType} ({CommandId}); it is not attended yet.",
                        companyKey,
                        command.Type,
                        command.Id);
                }
            }
            catch (FelApiException exception)
            {
                logger.LogWarning(
                    "The heartbeat of company {CompanyKey} failed ({StatusCode}).",
                    companyKey,
                    exception.StatusCode);
            }
        }
    }
}
