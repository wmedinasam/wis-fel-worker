using Microsoft.Extensions.Options;

namespace WisFel.Worker;

public sealed class WorkerOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Documents prepared per company and pass. The API accepts at most 100 per status batch.</summary>
    public int BatchSize { get; set; } = 50;

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan LicenseSyncInterval { get; set; } = TimeSpan.FromMinutes(10);

    public bool IsValid(out string error)
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            error = "Worker:PollInterval must be greater than zero.";
            return false;
        }

        if (BatchSize is < 1 or > 100)
        {
            error = "Worker:BatchSize must be between 1 and 100.";
            return false;
        }

        if (HeartbeatInterval <= TimeSpan.Zero || LicenseSyncInterval <= TimeSpan.Zero)
        {
            error = "Worker:HeartbeatInterval and Worker:LicenseSyncInterval must be greater than zero.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options) =>
        options.IsValid(out var error) ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
}
