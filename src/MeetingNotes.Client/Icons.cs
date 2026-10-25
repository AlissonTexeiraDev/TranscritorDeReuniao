using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MeetingNotes.Client;

/// <summary>Ícones desenhados em tempo de execução: cinza (parado) e vermelho (gravando).</summary>
internal static class Icons
{
    public static readonly Icon Idle = Make(Color.FromArgb(120, 120, 120));
    public static readonly Icon Recording = Make(Color.FromArgb(220, 40, 40));

    private static Icon Make(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 3, 3, 26, 26);
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawEllipse(pen, 3, 3, 26, 26);
            // "microfone" estilizado no centro
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 13, 8, 6, 11);
            g.FillEllipse(white, 13, 5, 6, 6);
            g.FillEllipse(white, 13, 16, 6, 6);
            g.FillRectangle(white, 15, 22, 2, 4);
            g.FillRectangle(white, 11, 25, 10, 2);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(handle);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
