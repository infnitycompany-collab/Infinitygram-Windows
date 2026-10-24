using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Infinitygram.Application;

namespace Infinitygram.App.ViewModels;

public partial class JoinViewModel : ViewModelBase
{
    [ObservableProperty] private string _groupLink = "";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
        => ResultText = string.IsNullOrWhiteSpace(GroupLink)
            ? "أدخل رابط الجروب."
            : "الاتصال الحي غير مضبوط — أضف حسابًا وأكمل تسجيل الدخول أولًا.";
}

public partial class SendViewModel : ViewModelBase
{
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
        => ResultText = string.IsNullOrWhiteSpace(MessageText)
            ? "أدخل نص الرسالة."
            : $"الرسالة: {MessageText.Length} حرف — الاتصال الحي غير مضبوط.";
}

public partial class ReadViewModel : ViewModelBase
{
    [ObservableProperty] private string _chatId = "";
    [ObservableProperty] private string _countText = "20";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
        => ResultText = "الاتصال الحي غير مضبوط — أضف حسابًا وأكمل تسجيل الدخول أولًا.";
}

public partial class ProfileViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _bio = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
        => ResultText = UsernameGenerator.IsValid(Username.Trim().ToLowerInvariant())
            ? "اليوزر صالح."
            : "اليوزر غير صالح (5–32 حرفًا، يبدأ بحرف، أحرف وأرقام و_ فقط).";
}

public partial class GroupsViewModel : ViewModelBase
{
    [ObservableProperty] private string _groupName = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
        => ResultText = UsernameGenerator.IsValid(Username.Trim().ToLowerInvariant())
            ? $"خطة إنشاء: {GroupName} (@{Username})"
            : "اليوزر غير صالح.";
}

public partial class ViewsViewModel : ViewModelBase
{
    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private string _emoji = "🔥";
    [ObservableProperty] private string _countText = "100";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
    {
        var ok = int.TryParse(CountText, out var c) && c is >= 1 and <= 100000;
        ResultText = ok
            ? $"خطة تعزيز: {Channel} — {Emoji} × {c} — الاتصال الحي غير مضبوط."
            : "العدد يجب أن يكون بين 1 و 100000.";
    }
}

public partial class ConverterViewModel : ViewModelBase
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _resultText = "";
    [RelayCommand] private void Run()
    {
        if (!File.Exists(FilePath)) { ResultText = "الملف غير موجود."; return; }
        var info = new FileInfo(FilePath);
        ResultText = $"الملف موجود — {info.Length} بايت — الامتداد: {info.Extension}";
    }
}
