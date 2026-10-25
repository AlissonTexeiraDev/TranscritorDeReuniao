namespace MeetingNotes.Client;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\TranscritorDeReuniao.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O Transcritor de Reunião já está aberto. Procure o ícone na bandeja do sistema, ao lado do relógio.",
                "Transcritor de Reunião", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayContext(startMinimized));
    }
}
