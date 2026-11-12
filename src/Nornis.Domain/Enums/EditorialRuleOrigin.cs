namespace Nornis.Domain.Enums;

/// <summary>Where a standing instruction came from (feature 44).</summary>
public enum EditorialRuleOrigin
{
    /// <summary>Written with a check-in answer; <c>EditorialRule.CheckInQuestionId</c> names the question.</summary>
    CheckIn,

    /// <summary>Typed by the GM on the Check-in page.</summary>
    Manual
}
