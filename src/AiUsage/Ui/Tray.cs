using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AlData.AiUsage.Ui;

/// <summary>The tray icon: a ring filled by the worst visible gauge, in its level color.</summary>
internal sealed class Tray : IDisposable
{
    const int Size = 32;
    const int MaxTip = 127;
    static readonly Color TrackColor = Color.FromArgb(110, 110, 110);
    readonly NotifyIcon icon = new();
    IntPtr handle;

    public event Action? LeftClick;
    public event Action? RightClick;

    public Tray()
    {
        icon.Text = "al-data-ai-usage";
        SetIcon(null, TrackColor);
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                LeftClick?.Invoke();
            else if (e.Button == MouseButtons.Right)
                RightClick?.Invoke();
        };
        icon.Visible = true;
    }

    public void Update(double? fraction, System.Windows.Media.Color color, string tip)
    {
        SetIcon(fraction, Color.FromArgb(color.R, color.G, color.B));
        icon.Text = tip.Length > MaxTip ? tip[..MaxTip] : tip;
    }

    /// <summary>The shell copies the icon, so the old handle can go once the new one is set.</summary>
    void SetIcon(double? fraction, Color color)
    {
        var previous = handle;
        icon.Icon = Draw(fraction, color);
        if (previous != IntPtr.Zero)
            Native.DestroyIcon(previous);
    }

    /// <summary>A 4 px gray ring with the arc from the top, the same picture as the tiles.</summary>
    Icon Draw(double? fraction, Color color)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(5, 5, 21, 21);
            using (var track = new Pen(TrackColor, 4))
                g.DrawEllipse(track, box);
            if (fraction is > 0)
            {
                using var arc = new Pen(color, 4);
                g.DrawArc(arc, box, -90, (float)(359.9 * Math.Min(1, fraction.Value)));
            }
        }
        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        if (handle != IntPtr.Zero)
            Native.DestroyIcon(handle);
        handle = IntPtr.Zero;
    }
}
