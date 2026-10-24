namespace Infinitygram.Domain;

/// <summary>Extraction coverage policy from the Infinitygram spec: new bot jobs finish
/// automatically at 97% distinct coverage; IsComplete stays false until a true 100%.</summary>
public static class CoveragePolicy
{
    public static long ThresholdFor(long totalMembers)
    {
        if (totalMembers < 0) throw new ArgumentOutOfRangeException(nameof(totalMembers));
        return (totalMembers * 97 + 99) / 100; // ceil(total * 97 / 100)
    }

    public static bool HasReachedThreshold(long distinctFetched, long totalMembers)
        => totalMembers > 0 && distinctFetched >= ThresholdFor(totalMembers);

    public static bool IsComplete(long distinctFetched, long totalMembers)
        => totalMembers > 0 && distinctFetched >= totalMembers;
}

/// <summary>Shared per-account add quotas from the spec: 50/hour, 200/day.
/// Failed attempts count conservatively (they consume quota).</summary>
public static class QuotaPolicy
{
    public const int HourlyLimit = 50;
    public const int DailyLimit = 200;
    public const int AddChunkSize = 20;

    public static bool CanAttempt(int usedInHour, int usedInDay)
        => usedInHour < HourlyLimit && usedInDay < DailyLimit;
}
