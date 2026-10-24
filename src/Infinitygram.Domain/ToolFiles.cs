namespace Infinitygram.Domain;

/// <summary>
/// Per-account tools files: every account gets its own file in tools/,
/// named after the group, holding the FULL pull for that account+group.
/// No shared file across accounts, no phone numbers in filenames
/// (account internal id only), no silent truncation: the file's header
/// metadata always states fetched/total/isComplete.
/// </summary>
public static class ToolFiles
{
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "group";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray());
        cleaned = string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd('_');
        return cleaned.Length == 0 ? "group" : cleaned;
    }

    public static string PathFor(string accountId, string groupName)
        => $"tools/{Sanitize(accountId)}/{Sanitize(groupName)}.csv";

    public static string PathFor(string accountId, string groupName, string extension)
        => $"tools/{Sanitize(accountId)}/{Sanitize(groupName)}.{extension.TrimStart('.')}";
}

/// <summary>One extraction route: job + account + group + durable checkpoint.
/// A route guarantees a full pull by never claiming completion below 100%:
/// 97% is only the auto-finish threshold for bot jobs; a tool file below 100%
/// is always marked PartialSuccess with its exact fetched/total.</summary>
public sealed record ExtractionRoute(
    Guid JobId, string AccountId, string GroupId, string GroupName,
    long CheckpointOffset, long DistinctFetched, long TotalMembers)
{
    public string ToolPath => ToolFiles.PathFor(AccountId, GroupName);
    public bool HasReached97 => CoveragePolicy.HasReachedThreshold(DistinctFetched, TotalMembers);
    public bool IsFullPull => CoveragePolicy.IsComplete(DistinctFetched, TotalMembers);
    public string FileStatus => IsFullPull ? "Complete" : HasReached97 ? "PartialSuccess-97" : "Partial";

    public ExtractionRoute Advance(long newOffset, long newDistinct)
        => this with { CheckpointOffset = newOffset, DistinctFetched = newDistinct };
}
