namespace Nornis.Worker.Configuration;

public class WorkerOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string QueueName { get; set; } = "source-extraction";

    public int MaxConcurrentCalls { get; set; } = 1;

    public int PrefetchCount { get; set; }

    public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Lock-renewal ceiling for the library-indexing queue, which needs far longer than
    /// extraction: a book-sized PDF is dozens of serial embedding round trips, and if the lock
    /// lapses mid-run Service Bus redelivers and a second full indexing run starts *alongside*
    /// the first — double-paying every embedding, because the document stays in
    /// <c>Indexing</c> status for the whole run and so cannot act as a guard.
    ///
    /// This is a ceiling, not a cost: short runs release their lock as soon as they complete,
    /// so a generous value here is free.
    /// </summary>
    public TimeSpan LibraryMaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(30);
}
