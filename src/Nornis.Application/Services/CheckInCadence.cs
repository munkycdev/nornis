namespace Nornis.Application.Services;

/// <summary>
/// The one counter behind "is a check-in due" (feature 44): how many dated sessions carry a
/// date later than the anchor. No anchor means every dated session counts, which is how a world
/// that has never checked in becomes due. Also the entry rule's "too new to judge" cut.
/// </summary>
public static class CheckInCadence
{
    public static int SessionsSince(IEnumerable<DateTimeOffset> sessionDates, DateTimeOffset? anchor) =>
        anchor is { } since ? sessionDates.Count(d => d > since) : sessionDates.Count();

    public static bool IsDue(int sessionsSince, int cadenceSessions) =>
        cadenceSessions > 0 && sessionsSince >= cadenceSessions;
}
