namespace Nornis.Application.Configuration;

public class AiBudgetOptions
{
    public const string SectionName = "AiBudget";

    /// <summary>
    /// Daily per-world AI spend ceiling in USD, summed across all members and all
    /// operation types (ask, extraction, continuity audit), for worlds that have not set
    /// their own. Resets at midnight UTC.
    ///
    /// <c>null</c> — and only null — means no ceiling. Zero used to mean that too, which put
    /// this setting at odds with the public-Ask cap, where zero means the opposite: blocked.
    /// Zero now blocks here as well, so "spend nothing" reads the same in both places and a
    /// zero arriving from anywhere fails closed.
    /// </summary>
    public decimal? DailyWorldBudgetUsd { get; set; } = 2.00m;
}
