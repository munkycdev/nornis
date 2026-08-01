namespace Nornis.Web.Services;

/// <summary>Compact "how long ago" stamps for lists and panels.</summary>
public static class RelativeTime
{
    public static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.UtcNow - t.ToUniversalTime();
        if (d.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)d.TotalMinutes)}m ago";
        }
        if (d.TotalHours < 24)
        {
            return $"{(int)d.TotalHours}h ago";
        }
        if (d.TotalDays < 2)
        {
            return "Yesterday";
        }
        return $"{(int)d.TotalDays}d ago";
    }
}
