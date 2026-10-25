using Microsoft.Win32;

namespace MeetingNotes.Startup;

/// <summary>Registra o cliente para iniciar junto com o Windows (chave Run do usuário atual).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TranscritorDeReuniao";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --minimized");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
