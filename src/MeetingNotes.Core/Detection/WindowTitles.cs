using System.Runtime.InteropServices;
using System.Text;

namespace MeetingNotes.Detection;

/// <summary>Títulos das janelas visíveis de um processo e heurísticas para extrair o nome da reunião.</summary>
public static class WindowTitles
{
    private static readonly HashSet<string> NavSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chat", "Atividade", "Activity", "Equipes", "Teams", "Calendário", "Calendar", "Chamadas", "Calls",
        "Arquivos", "Files", "OneDrive", "Aplicativos", "Apps", "Microsoft Teams", "Copilot",
        "Comunidades", "Communities", "Planner", "Tarefas", "Tasks"
    };

    private static readonly string[] BrowserSuffixes =
    [
        " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Mozilla Firefox", " - Brave", " - Opera", " - Vivaldi"
    ];

    public static IReadOnlyList<string> ForProcess(int pid)
    {
        var titles = new List<string>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var windowPid);
            if ((int)windowPid != pid) return true;
            var len = GetWindowTextLengthW(hWnd);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowTextW(hWnd, sb, sb.Capacity);
            var title = sb.ToString().Replace("​", "").Trim();
            if (title.Length > 0) titles.Add(title);
            return true;
        }, IntPtr.Zero);
        return titles;
    }

    /// <summary>Escolhe um nome de reunião a partir do app e dos títulos de janela.</summary>
    public static string BestTitle(string app, IReadOnlyList<string> titles)
    {
        var a = app.ToLowerInvariant();
        var isTeams = a.Contains("teams");
        var isBrowser = a is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi";

        if (isTeams || isBrowser)
        {
            // Teams (app ou web): "Nome da reunião | Microsoft Teams"
            foreach (var t in titles)
            {
                var idx = t.IndexOf("Microsoft Teams", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var name = t[..idx].Trim(' ', '|', '-', '—', '–');
                var first = name.Split('|')[0].Trim();
                if (name.Length > 0 && !NavSections.Contains(first) && !NavSections.Contains(name)) return name;
            }

            // Google Meet: "Meet – abc-defg-hij - Google Chrome" ou "Nome da chamada - Meet - Google Chrome"
            foreach (var t in titles)
            {
                if (t.StartsWith("Meet", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Google Meet", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains(" - Meet", StringComparison.OrdinalIgnoreCase))
                {
                    var name = StripBrowserSuffix(t).Trim();
                    return name.Length > 0 ? name : "Reunião no Google Meet";
                }
            }

            return isTeams ? "Reunião no Teams" : "Reunião no navegador";
        }

        if (a.Contains("zoom")) return "Reunião no Zoom";
        return $"Reunião ({app})";
    }

    private static string StripBrowserSuffix(string title)
    {
        foreach (var suffix in BrowserSuffixes)
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return title[..^suffix.Length];
        return title;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
