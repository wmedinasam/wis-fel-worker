namespace WisFel.Worker;

public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
