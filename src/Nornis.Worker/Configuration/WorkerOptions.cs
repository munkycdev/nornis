namespace Nornis.Worker.Configuration;

public class WorkerOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string QueueName { get; set; } = "source-extraction";

    public int MaxConcurrentCalls { get; set; } = 1;

    public int PrefetchCount { get; set; }

    public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);
}
