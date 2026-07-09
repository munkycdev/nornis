namespace Nornis.Application.Configuration;

public class AiBudgetOptions
{
    public const string SectionName = "AiBudget";

    /// <summary>
    /// Daily per-world AI spend ceiling in USD, summed across all members and all
    /// operation types (ask, extraction, continuity audit). Zero or negative disables
    /// the guard. Resets at midnight UTC.
    /// </summary>
    public decimal DailyWorldBudgetUsd { get; set; } = 2.00m;
}
