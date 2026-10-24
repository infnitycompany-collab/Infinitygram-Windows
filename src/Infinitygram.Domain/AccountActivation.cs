namespace Infinitygram.Domain;

/// <summary>
/// Active Accounts — the differentiator (owner order 17:26).
/// A "super" account is not declared by a flag; it is EARNED by a checklist
/// on that account's OWN assets only:
///   profile: name + photo + bio + username (4/4)
///   own spaces: 2 groups + 2 private channels created by that account
///   activity: at least 1 message posted in each of those 4 spaces (4+ total)
/// Only when all of it is done does Status become Super. Anything less is
/// Cold / Warming / Active, honestly reported — never faked.
/// No activity is ever sent to anyone else's group/channel: all spaces are
/// created by the account itself, and all messages are its own posts there.
/// </summary>
public sealed record AccountActivation(
    string AccountId,
    bool NameSet, bool PhotoSet, bool BioSet, bool UsernameSet,
    int GroupsCreated, int PrivateChannelsCreated, int MessagesPosted)
{
    public const int RequiredGroups = 2;
    public const int RequiredPrivateChannels = 2;
    public const int RequiredMessages = RequiredGroups + RequiredPrivateChannels; // 1 per space

    public int ProfileScore => (NameSet ? 1 : 0) + (PhotoSet ? 1 : 0) + (BioSet ? 1 : 0) + (UsernameSet ? 1 : 0);
    public bool ProfileComplete => ProfileScore == 4;
    public bool SpacesComplete => GroupsCreated >= RequiredGroups && PrivateChannelsCreated >= RequiredPrivateChannels;
    public bool ActivityComplete => MessagesPosted >= RequiredMessages;

    public string Status
    {
        get
        {
            if (ProfileComplete && SpacesComplete && ActivityComplete) return "Super";
            if (ProfileComplete && SpacesComplete) return "Active";
            if (ProfileScore > 0 || GroupsCreated > 0 || PrivateChannelsCreated > 0) return "Warming";
            return "Cold";
        }
    }

    public int CompletionPercent
    {
        get
        {
            // 40% profile + 40% spaces + 20% activity = 100% only at Super.
            var profile = ProfileScore * 10;
            var spaces = Math.Min(GroupsCreated, RequiredGroups) * 10
                       + Math.Min(PrivateChannelsCreated, RequiredPrivateChannels) * 10;
            var activity = Math.Min(MessagesPosted, RequiredMessages) * 5;
            return Math.Min(100, profile + spaces + activity);
        }
    }

    public IReadOnlyList<string> MissingSteps
    {
        get
        {
            var missing = new List<string>();
            if (!NameSet) missing.Add("name");
            if (!PhotoSet) missing.Add("photo");
            if (!BioSet) missing.Add("bio");
            if (!UsernameSet) missing.Add("username");
            if (GroupsCreated < RequiredGroups) missing.Add($"groups:{RequiredGroups - GroupsCreated}-left");
            if (PrivateChannelsCreated < RequiredPrivateChannels) missing.Add($"private-channels:{RequiredPrivateChannels - PrivateChannelsCreated}-left");
            if (MessagesPosted < RequiredMessages) missing.Add($"messages:{RequiredMessages - MessagesPosted}-left");
            return missing;
        }
    }
}
