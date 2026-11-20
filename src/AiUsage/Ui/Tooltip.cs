using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Ui;

/// <summary>Hover tooltip with the full data. It hides by where the cursor really is, not on a
/// mouse leave: a window appearing beside the cursor can raise a leave of its own.</summary>
internal sealed class Tooltip(Func<WidgetWindow> current, Func<string> text, Func<Theme> theme)
{
    /// <summary>The widget's window is replaced when its transparency changes.</summary>
    WidgetWindow widget => current();

    static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(350);
    static readonly TimeSpan WatchEvery = TimeSpan.FromMilliseconds(300);
    /// <summary>Between the tooltip and the widget, and from the screen edge.</summary>
    const double Gap = 6, Margin = 4;

    DispatcherTimer? pending, watch;
    TipWindow? window;

    public bool IsOpen => window is not null;

    /// <summary>The pointer came in: show after a moment, if it stays.</summary>
    public void Schedule()
    {
        if (window is not null || pending is not null)
            return;
        pending = new DispatcherTimer { Interval = Delay };
        pending.Tick += (_, _) => Show();
        pending.Start();
    }

    /// <summary>The pointer went out before the tooltip showed. An open one is the watch's business.</summary>
    public void Leave() => CancelShow();

    public void Hide()
    {
        CancelShow();
        watch?.Stop();
        watch = null;
        window?.Close();
        window = null;
    }

    void CancelShow()
    {
        pending?.Stop();
        pending = null;
    }

    bool PointerInside() =>
        Native.GetCursorPos(out var p) && Native.GetWindowRect(widget.Handle, out var r)
        && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    void Show()
    {
        CancelShow();
        if (!PointerInside())
            return;
        var content = text();
        if (content.Length == 0)
            return;
        Hide();
        var colors = theme();
        window = new TipWindow(content, colors.Text, colors.Dim, widget.PixelsPerDip);
        Place(window);
        window.Show();
        watch = new DispatcherTimer { Interval = WatchEvery };
        watch.Tick += (_, _) =>
        {
            if (!PointerInside())
                Hide();
        };
        watch.Start();
    }

    /// <summary>Above the widget, below it where the screen ends; left edges aligned.</summary>
    void Place(TipWindow tip)
    {
        if (!Native.GetWindowRect(widget.Handle, out var rect))
            return;
        var gap = (int)Math.Round(Gap * tip.Ppd);
        var margin = (int)Math.Round(Margin * tip.Ppd);
        var y = rect.Top - tip.PixelHeight - gap;
        if (y < 0)
            y = rect.Bottom + gap;
        var screenW = Native.GetSystemMetrics(Native.SmCxScreen);
        var x = Math.Max(margin, Math.Min(rect.Left, screenW - tip.PixelWidth - margin));
        tip.Move(x, y);
    }

    /// <summary>A multi-line Tk label in a one pixel frame. The label has a two pixel border inside
    /// its padding; lines are a GDI line box apart, left aligned.</summary>
    sealed class TipWindow : Window
    {
        /// <summary>Monospace: the lines align their columns with spaces.</summary>
        const string Face = "Consolas";
        const double Edge = 1, Border = 2, PadX = 10, PadY = 8, TextSize = 12;
        static readonly Brush Fill = Frozen(Color.FromRgb(0x2b, 0x2b, 0x2b));
        static readonly FontFamily Family = new(Face);

        sealed class Surface(TipWindow tip) : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc) => tip.Draw(dc);
        }

        readonly TkText.Run[] lines;
        readonly IntPtr handle;
        readonly int edge, insetX, insetY, labelW, labelH, line, ascent;

        public double Ppd { get; }
        public int PixelWidth => labelW + 2 * edge;
        public int PixelHeight => labelH + 2 * edge;

        public TipWindow(string text, Color foreground, Color frame, double ppd)
        {
            Ppd = ppd;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Frozen(frame);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
            Content = new Surface(this);
            var brush = Frozen(foreground);
            lines = text.Split('\n').Select(l => TkText.Format(l, TextSize, false, brush, ppd, Family)).ToArray();
            int descent;
            (ascent, descent) = TkDraw.Metrics(Face, (int)Math.Round(TextSize * ppd));
            line = ascent + descent;
            edge = Px(Edge);
            insetX = Px(Border + PadX);
            insetY = Px(Border + PadY);
            labelW = lines.Max(l => l.Pixels) + 2 * insetX;
            labelH = lines.Length * line + 2 * insetY;
            handle = new WindowInteropHelper(this).EnsureHandle();
            var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64()
                        | Native.WsExToolWindow | Native.WsExNoActivate;
            Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style));
        }

        int Px(double dip) => (int)Math.Round(dip * Ppd);

        public void Move(int x, int y) =>
            Native.SetWindowPos(handle, IntPtr.Zero, x, y, PixelWidth, PixelHeight, Native.SwpNoZOrder | Native.SwpNoActivate);

        void Draw(DrawingContext dc)
        {
            dc.DrawRectangle(Fill, null, new Rect(edge / Ppd, edge / Ppd, labelW / Ppd, labelH / Ppd));
            for (var i = 0; i < lines.Length; i++)
                TkText.DrawOnBaseline(dc, lines[i], edge + insetX, edge + insetY + i * line + ascent);
        }

        static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
