using System.Runtime.InteropServices;

namespace Infinitygram.App;

/// <summary>Crash log: if the app ever closes with an error, the reason is
/// written here instead of vanishing silently. Two copies: next to the exe
/// and in %AppData%/Infinitygram/crash.log. Messages pass through the
/// secret redactor so a crash can never leak a hash, token or OTP.</summary>
public static class CrashLog
{
    public static void Write(Exception ex)
    {
        try
        {
            var text = $"[{DateTimeOffset.UtcNow:O}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n";
            text = Infinitygram.Application.SecretRedactor.Redact(text);
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Infinitygram");
            Directory.CreateDirectory(appData);
            File.AppendAllText(Path.Combine(appData, "crash.log"), text);
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), text); }
            catch { /* exe folder may be read-only — AppData copy is enough */ }
        }
        catch { /* crash logging must never crash */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void ShowOnWindows(string message)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { MessageBoxW(IntPtr.Zero, message, "Infinitygram — خطأ", 0x10); } catch { }
    }
}
