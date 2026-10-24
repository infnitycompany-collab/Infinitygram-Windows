using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Infinitygram.Application;
using Infinitygram.Domain;
using Infinitygram.Infrastructure;

namespace Infinitygram.App.ViewModels;

/// <summary>Accounts panel — real local functionality, no live Telegram needed.
/// api_hash is typed once, sealed by the Vault, and NEVER shown back.
/// The DB lives in %AppData%/Infinitygram/infinitygram.db (created on first use).
/// Master key note: in this phase the program uses a local program key;
/// the owner sets his own program lock password in P1-complete (Settings).</summary>
public partial class AccountsViewModel : ViewModelBase
{
    private const string ProgramKey = "infinitygram-local-program-key-v1";
    private readonly Lazy<(InfinitygramDbContext Db, AccountStore Store)> _store;

    private readonly Action? _onChanged;
    public AccountsViewModel(Action? onChanged = null)
    {
        _onChanged = onChanged;
        _store = new Lazy<(InfinitygramDbContext, AccountStore)>(() =>
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Infinitygram");
            Directory.CreateDirectory(dir);
            var db = InfinitygramDbContext.ForFile(Path.Combine(dir, "infinitygram.db"));
            var vault = new VaultService(new InMemoryAuditStore());
            return (db, new AccountStore(db, vault));
        });
    }

    [ObservableProperty] private string _accountId = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _apiIdText = "";
    [ObservableProperty] private string _apiHash = "";
    [ObservableProperty] private string _statusText = "";
    public ObservableCollection<string> AccountLines { get; } = new();
    // Safe by design: a database failure returns an empty list and a
    // visible status message — it must NEVER crash the whole app.
    public IReadOnlyList<string> AccountIds
    {
        get
        {
            try { return _store.Value.Store.ListAccounts().Select(a => a.Id).ToList(); }
            catch (Exception ex)
            {
                StatusText = "تعذر فتح قاعدة البيانات: " + ex.Message;
                return Array.Empty<string>();
            }
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            AccountLines.Clear();
            foreach (var a in _store.Value.Store.ListAccounts())
                AccountLines.Add($"{a.Id} — {a.DisplayName} — {a.Status} — api_id: {a.ApiId}");
            StatusText = AccountLines.Count == 0 ? "لا توجد حسابات بعد." : $"{AccountLines.Count} حساب محفوظ.";
            _onChanged?.Invoke();
        }
        catch (Exception ex) { StatusText = "خطأ: " + ex.Message; }
    }

    [RelayCommand]
    private void AddAccount()
    {
        try
        {
            if (!long.TryParse(ApiIdText, out var apiId) || apiId <= 0)
            { StatusText = "api_id يجب أن يكون رقمًا أكبر من صفر."; return; }
            if (ApiHash.Length != 32)
            { StatusText = "api_hash يجب أن يكون 32 حرفًا بالضبط (hex)."; return; }
            _store.Value.Store.AddAccount(AccountId.Trim(), DisplayName.Trim(), apiId, ApiHash.Trim(), ProgramKey);
            ApiHash = ""; // never keep the plain value in memory longer than needed
            StatusText = $"تم حفظ الحساب {AccountId} مشفرًا.";
            Refresh();
        }
        catch (Exception ex) { StatusText = "خطأ: " + SecretRedactor.Redact(ex.Message); }
    }

    [RelayCommand]
    private void DeleteAccount()
    {
        try
        {
            _store.Value.Store.DeleteAccount(AccountId.Trim());
            StatusText = $"تم حذف الحساب {AccountId} نهائيًا.";
            Refresh();
        }
        catch (Exception ex) { StatusText = "خطأ: " + ex.Message; }
    }
}

/// <summary>Extraction panel — wired to the real ExtractionEngine.
/// With no live session configured, Run fails fast with the clear
/// "not configured" message (never a fake extraction).</summary>
public partial class ExtractionViewModel : ViewModelBase
{
    private readonly Action<string>? _onReport;
    public ExtractionViewModel(Action<string>? onReport = null) => _onReport = onReport;

    [ObservableProperty] private string _groupId = "";
    [ObservableProperty] private string _totalMembersText = "";
    [ObservableProperty] private string _statusText = "جاهز.";
    [ObservableProperty] private string _resultText = "";

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            if (!long.TryParse(TotalMembersText, out var total) || total <= 0)
            { StatusText = "أدخل إجمالي أعضاء صحيحًا أكبر من صفر."; return; }
            StatusText = "جارٍ الاستخراج...";
            var engine = new ExtractionEngine();
            var client = new WTelegramUserbotClient();
            var (route, distinct) = await engine.ExtractAsync(
                client, "selected", GroupId.Trim(), GroupId.Trim(), total, null, 100, CancellationToken.None);
            ResultText = $"الحالة: {route.FileStatus} | مميز: {route.DistinctFetched}/{total} | checkpoint: {route.CheckpointOffset}";
            StatusText = "انتهى.";
            _onReport?.Invoke(ResultText);
            _ = distinct;
        }
        catch (Exception ex) { StatusText = "خطأ: " + ex.Message; ResultText = ""; }
    }
}

/// <summary>Add panel — wired to the real AddEngine + RoundRobin + report.
/// With no accounts configured, every member ends Uncertain
/// ("no-account-available") and the report says so — never fake Added.</summary>
public partial class AddViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<string>> _getAccounts;
    private readonly Action<string>? _onReport;
    public AddViewModel(Func<IReadOnlyList<string>> getAccounts, Action<string>? onReport = null)
    {
        _getAccounts = getAccounts;
        _onReport = onReport;
    }

    [ObservableProperty] private string _groupName = "";
    [ObservableProperty] private string _membersText = "";
    [ObservableProperty] private string _statusText = "جاهز.";
    [ObservableProperty] private string _resultText = "";

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            var members = MembersText.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(m => m.Trim()).Where(m => m.Length > 0).ToList();
            if (members.Count == 0) { StatusText = "أدخل عضوًا واحدًا على الأقل (سطر لكل عضو)."; return; }
            var accounts = _getAccounts();
            if (accounts.Count == 0)
            {
                // Still run the engine so the report is real, not a hand-typed guess.
                var engine = new AddEngine();
                var pool = new RoundRobinPool(Array.Empty<string>());
                var (filtered, report) = await engine.AddAsync(
                    new WTelegramUserbotClient(), pool, members, GroupName.Trim(),
                    () => DateTimeOffset.UtcNow, CancellationToken.None);
                ResultText = report.ToCleanText() + "\n(لا توجد حسابات مضافة — أضف حسابًا من وحدة الأكونتات أولًا)";
                StatusText = "انتهى بتقرير (بلا حسابات).";
                _onReport?.Invoke(ResultText);
                _ = filtered;
                return;
            }
            StatusText = "جارٍ الإضافة...";
            var addEngine = new AddEngine();
            var roundRobin = new RoundRobinPool(accounts);
            var (f2, r2) = await addEngine.AddAsync(
                new WTelegramUserbotClient(), roundRobin, members, GroupName.Trim(),
                () => DateTimeOffset.UtcNow, CancellationToken.None);
            ResultText = r2.ToCleanText();
            StatusText = "انتهى.";
            _onReport?.Invoke(ResultText);
            _ = f2;
        }
        catch (Exception ex) { StatusText = "خطأ: " + ex.Message; }
    }
}

/// <summary>Active Accounts panel — pure domain, works fully in the UI.
/// Status/percent/missing steps are computed from AccountActivation,
/// the same type the tests prove (no separate UI logic to drift).</summary>
public partial class ActivationViewModel : ViewModelBase
{
    [ObservableProperty] private bool _nameSet;
    [ObservableProperty] private bool _photoSet;
    [ObservableProperty] private bool _bioSet;
    [ObservableProperty] private bool _usernameSet;
    [ObservableProperty] private string _groupsText = "0";
    [ObservableProperty] private string _channelsText = "0";
    [ObservableProperty] private string _messagesText = "0";
    [ObservableProperty] private string _resultText = "املأ القائمة ثم اضغط حساب الحالة.";

    [RelayCommand]
    private void Compute()
    {
        var g = int.TryParse(GroupsText, out var gv) ? gv : 0;
        var c = int.TryParse(ChannelsText, out var cv) ? cv : 0;
        var m = int.TryParse(MessagesText, out var mv) ? mv : 0;
        var a = new AccountActivation("ui", NameSet, PhotoSet, BioSet, UsernameSet, g, c, m);
        ResultText = $"الحالة: {a.Status} | الإنجاز: {a.CompletionPercent}% | النواقص: " +
            (a.MissingSteps.Count == 0 ? "لا شيء — Super" : string.Join(", ", a.MissingSteps));
    }
}
