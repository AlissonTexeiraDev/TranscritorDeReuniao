namespace MeetingNotes.Client;

/// <summary>Pergunta o título da reunião antes de uma gravação manual.</summary>
internal sealed class TitleDialog : Form
{
    private readonly TextBox _text = new();

    private TitleDialog(string defaultTitle)
    {
        Text = "Iniciar gravação";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 120);
        Icon = Icons.Recording;

        var label = new Label { Text = "Título da reunião:", AutoSize = true, Location = new Point(12, 14) };
        _text.Location = new Point(12, 36);
        _text.Width = 396;
        _text.Text = defaultTitle;
        _text.SelectAll();

        var ok = new Button { Text = "Gravar", DialogResult = DialogResult.OK, Location = new Point(236, 78), Width = 84 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(324, 78), Width = 84 };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([label, _text, ok, cancel]);
    }

    public static string? Ask(IWin32Window? owner, string defaultTitle)
    {
        using var dialog = new TitleDialog(defaultTitle);
        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result != DialogResult.OK) return null;
        var title = dialog._text.Text.Trim();
        return title.Length == 0 ? defaultTitle : title;
    }
}
