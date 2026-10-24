using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Infinitygram.App.ViewModels;

public sealed record NavModule(string Key, string TitleAr, string TitleEn);

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<NavModule> Modules { get; } = new()
    {
        new("dashboard", "الرئيسية", "Dashboard"),
        new("accounts", "الأكونتات", "Accounts"),
        new("active", "الحسابات النشطة", "Active Accounts"),
        new("search", "البحث", "Search"),
        new("join", "الانضمام للجروبات", "Join Groups"),
        new("extract", "الاستخراج", "Extraction"),
        new("filter", "الفلترة", "Filter"),
        new("send", "إرسال الرسائل", "Send Messages"),
        new("add", "الإضافة", "Add Members"),
        new("read", "قراءة الرسائل", "Read Messages"),
        new("profile", "البروفايل", "Profile"),
        new("groups", "إدارة الجروبات", "Group Admin"),
        new("proxies", "البروكسيات", "Proxies"),
        new("views", "المشاهدات والتفاعلات", "Views & Reactions"),
        new("reports", "التقارير", "Reports"),
        new("settings", "الإعدادات", "Settings"),
        new("converter", "محوّل السيشنات", "Session Converter"),
    };

    [ObservableProperty] private NavModule? _selectedModule;
    [ObservableProperty] private bool _isArabic = true;
    [ObservableProperty] private string _statusAccount = "لا يوجد حساب مختار";
    [ObservableProperty] private string _statusQuota = "الحصة: 0/50 ساعة • 0/200 يوم";
    [ObservableProperty] private string _statusJob = "جاهز";

    public AccountsViewModel AccountsVM { get; }
    public ExtractionViewModel ExtractionVM { get; }
    public AddViewModel AddVM { get; }
    public ActivationViewModel ActivationVM { get; } = new();
    public FilterViewModel FilterVM { get; } = new();
    public SearchViewModel SearchVM { get; } = new();
    public ProxyViewModel ProxyVM { get; } = new();
    public SettingsViewModel SettingsVM { get; } = new();
    public JoinViewModel JoinVM { get; } = new();
    public SendViewModel SendVM { get; } = new();
    public ReadViewModel ReadVM { get; } = new();
    public ProfileViewModel ProfileVM { get; } = new();
    public GroupsViewModel GroupsVM { get; } = new();
    public ViewsViewModel ViewsVM { get; } = new();
    public ConverterViewModel ConverterVM { get; } = new();

    [ObservableProperty] private string _dashboardAccountCount = "—";
    [ObservableProperty] private string _dashboardReportCount = "—";
    [ObservableProperty] private string _dashboardModuleCount = "17";
    [ObservableProperty] private string _reportsText = "";

    private void UpdateDashboard()
    {
        try
        {
            var reports = ReportsText.Contains("---") ? ReportsText.Split("---").Length - 1 : 0;
            DashboardAccountCount = AccountsVM.AccountIds.Count.ToString();
            DashboardReportCount = reports.ToString();
        }
        catch { }
    }

    public bool IsAccountsVisible => SelectedModule?.Key == "accounts";
    public bool IsExtractionVisible => SelectedModule?.Key == "extract";
    public bool IsAddVisible => SelectedModule?.Key == "add";
    public bool IsActiveVisible => SelectedModule?.Key == "active";
    public bool IsReportsVisible => SelectedModule?.Key == "reports";
    public bool IsFilterVisible => SelectedModule?.Key == "filter";
    public bool IsSearchVisible => SelectedModule?.Key == "search";
    public bool IsProxyVisible => SelectedModule?.Key == "proxies";
    public bool IsSettingsVisible => SelectedModule?.Key == "settings";
    public bool IsDashboardVisible => SelectedModule?.Key == "dashboard";
    public bool IsJoinVisible => SelectedModule?.Key == "join";
    public bool IsSendVisible => SelectedModule?.Key == "send";
    public bool IsReadVisible => SelectedModule?.Key == "read";
    public bool IsProfileVisible => SelectedModule?.Key == "profile";
    public bool IsGroupsVisible => SelectedModule?.Key == "groups";
    public bool IsViewsVisible => SelectedModule?.Key == "views";
    public bool IsConverterVisible => SelectedModule?.Key == "converter";

    public string SelectedTitle => SelectedModule is null
        ? ""
        : (IsArabic ? SelectedModule.TitleAr : SelectedModule.TitleEn);

    [RelayCommand]
    private void ToggleLanguage()
    {
        IsArabic = !IsArabic;
        RefreshSelected();
    }

    partial void OnSelectedModuleChanged(NavModule? value) => RefreshSelected();
    partial void OnIsArabicChanged(bool value) => RefreshSelected();

    private void RefreshSelected()
    {
        OnPropertyChanged(nameof(SelectedTitle));
        foreach (var name in new[] { nameof(IsAccountsVisible), nameof(IsExtractionVisible), nameof(IsAddVisible),
            nameof(IsActiveVisible), nameof(IsReportsVisible), nameof(IsFilterVisible), nameof(IsSearchVisible),
            nameof(IsProxyVisible), nameof(IsSettingsVisible), nameof(IsDashboardVisible), nameof(IsJoinVisible),
            nameof(IsSendVisible), nameof(IsReadVisible), nameof(IsProfileVisible), nameof(IsGroupsVisible),
            nameof(IsViewsVisible), nameof(IsConverterVisible) })
            OnPropertyChanged(name);
    }

    public MainViewModel()
    {
        AccountsVM = new AccountsViewModel(UpdateDashboard);
        ExtractionVM = new ExtractionViewModel(t => { ReportsText = t + "\n---\n" + ReportsText; UpdateDashboard(); });
        AddVM = new AddViewModel(() => AccountsVM.AccountIds, t => { ReportsText = t + "\n---\n" + ReportsText; UpdateDashboard(); });
        SelectedModule = Modules[0];
    }
}
