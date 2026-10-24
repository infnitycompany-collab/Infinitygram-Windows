using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Infinitygram.Application;

namespace Infinitygram.App.ViewModels;

/// <summary>Filter panel: paste member lines (id,username,phone,hasTelegram)
/// as CSV-ish text, get the four splits. Pure local logic, fully proven.</summary>
public partial class FilterViewModel : ViewModelBase
{
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private string _resultText = "";

    [RelayCommand]
    private void Run()
    {
        var members = new List<MemberInfo>();
        foreach (var line in InputText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(',');
            if (p.Length < 4) continue;
            members.Add(new MemberInfo(p[0].Trim(),
                string.IsNullOrWhiteSpace(p[1]) ? null : p[1].Trim(),
                string.IsNullOrWhiteSpace(p[2]) ? null : p[2].Trim(),
                bool.TryParse(p[3].Trim(), out var b) && b));
        }
        var (withTg, withoutTg, withUser, withPhone) = MemberFilter.Split(members);
        ResultText = $"الإجمالي: {members.Count} | بتليجرام: {withTg.Count} | بلا تليجرام: {withoutTg.Count} | بيوزر: {withUser.Count} | بتليفون: {withPhone.Count}";
    }
}

/// <summary>Username Lab: generate valid, unique usernames locally.
/// Availability on Telegram is a live check (P3-live) — generation here
/// never claims a name is free.</summary>
public partial class SearchViewModel : ViewModelBase
{
    [ObservableProperty] private string _prefix = "inf";
    [ObservableProperty] private string _countText = "20";
    [ObservableProperty] private string _lengthText = "10";
    [ObservableProperty] private string _resultText = "";

    [RelayCommand]
    private void Run()
    {
        try
        {
            var count = int.TryParse(CountText, out var c) ? c : 0;
            var length = int.TryParse(LengthText, out var l) ? l : 0;
            if (count <= 0 || count > 500 || length < 5 || length > 32)
            { ResultText = "العدد 1–500 والطول 5–32."; return; }
            ResultText = string.Join("\n", UsernameGenerator.Generate(Prefix.Trim().ToLowerInvariant(), count, length));
        }
        catch (Exception ex) { ResultText = "خطأ: " + ex.Message; }
    }
}

/// <summary>Proxies panel: parse pasted proxies, show valid/invalid counts
/// and the next rotated proxy. Reachability is a live check (P3-live).</summary>
public partial class ProxyViewModel : ViewModelBase
{
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private string _resultText = "";
    private int _pointer;

    [RelayCommand]
    private void Run()
    {
        var proxies = InputText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ProxyPool.Parse).Where(p => p is not null).Select(p => p!).ToList();
        var total = InputText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        var next = ProxyPool.Next(proxies, ref _pointer, new Dictionary<string, int>());
        ResultText = $"صالح: {proxies.Count}/{total} | التالي بالتدوير: {(next?.Key ?? "لا يوجد")}";
    }
}

/// <summary>Settings / Throttling panel: validate a throttle plan and
/// compute exact rest time. The plan is what the send/add engines must obey.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string _delayText = "2";
    [ObservableProperty] private string _restAfterText = "10";
    [ObservableProperty] private string _restSecondsText = "60";
    [ObservableProperty] private string _messagesText = "100";
    [ObservableProperty] private string _resultText = "";

    [RelayCommand]
    private void Run()
    {
        var delay = int.TryParse(DelayText, out var d) ? d : -1;
        var after = int.TryParse(RestAfterText, out var a) ? a : 0;
        var secs = int.TryParse(RestSecondsText, out var s) ? s : -1;
        var msgs = int.TryParse(MessagesText, out var m) ? m : 0;
        var plan = new ThrottlePlan(delay, after, secs, 3, 120);
        ResultText = plan.IsValid
            ? $"الخطة صالحة: تأخير {delay} ث بعد كل رسالة، راحة {secs} ث بعد كل {after} رسائل — راحة إجمالية لـ {msgs} رسالة: {plan.TotalRestFor(msgs).TotalSeconds} ث."
            : "الخطة غير صالحة — راجع الأرقام.";
    }
}
