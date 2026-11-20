using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Ui;

/// <summary>Threshold toasts: once per threshold per reset cycle, stacked above the widget.</summary>
internal sealed class Toasts(Func<IntPtr> current)
{
    /// <summary>The widget's window is replaced when its transparency changes.</summary>
    IntPtr widget => current();

    static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(6);
    static readonly TimeSpan FadeEvery = TimeSpan.FromMilliseconds(40);
    const double FadeStep = 0.08;
    /// <summary>Between two toasts, and between a toast and the widget.</summary>
    const double Gap = 6;

    readonly Dictionary<string, (DateTimeOffset? Cycle, double Fired)> fired = [];
    readonly List<ToastWindow> windows = [];

    /// <summary>Call after every successful refresh. Thresholds are percentages, lowest first.</summary>
    public void Check(IEnumerable<Gauge> gauges, IReadOnlyList<double> thresholds, Theme theme, I18n i18n, bool byPace)
    {
        foreach (var gauge in gauges)
        {
            if (Model.Percent(gauge) is not { } pct)
                continue;
            var crossed = thresholds.Where(t => pct >= t).ToList();
            if (crossed.Count == 0)
                continue;
            var top = crossed[^1];
            var slot = $"{gauge.Provider}:{gauge.Account}:{gauge.Key}";
            var (cycle, done) = fired.TryGetValue(slot, out var seen) ? seen : (gauge.ResetsAt, 0);
            if (cycle != gauge.ResetsAt)
                done = 0;   // a new cycle: the thresholds apply again
            if (top > done)
                Show($"{i18n.GaugeLabel(gauge)}: {pct}%", Accent(gauge, theme, byPace), theme.Text);
            fired[slot] = (gauge.ResetsAt, Math.Max(done, top));
        }
    }

    /// <summary>The gauge color, by forecast when the widget colors by pace.</summary>
    static Color Accent(Gauge gauge, Theme theme, bool byPace)
    {
        if (byPace && Model.Pace(gauge) is { } forecast)
            return Model.LevelColor(forecast.Projected, theme.PaceLevels.Count > 0 ? theme.PaceLevels : theme.Levels, theme.Dim);
        return Model.LevelColor(gauge.Value, theme.Levels, theme.Dim);
    }

    public void Show(string text, Color accent, Color foreground)
    {
        var dpi = Native.GetDpiForWindow(widget);
        var toast = new ToastWindow(text, accent, foreground, dpi == 0 ? 1 : dpi / 96.0);
        windows.Add(toast);
        Place(toast.Ppd);
        toast.Show();
        Fade(toast, 0, 1, false);
        var timer = new DispatcherTimer { Interval = ShowFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Fade(toast, 1, 0, true);
        };
        timer.Start();
    }

    /// <summary>Newest nearest to the widget, the rest above it; below the widget where the screen ends.</summary>
    void Place(double ppd)
    {
        if (!Native.GetWindowRect(widget, out var rect))
            return;
        var gap = (int)Math.Round(Gap * ppd);
        var y = rect.Top;
        for (var i = windows.Count - 1; i >= 0; i--)
        {
            y -= windows[i].PixelHeight + gap;
            if (y < 0)
                y = rect.Bottom + gap;
            windows[i].Move(rect.Left, y);
        }
    }

    /// <summary>The first step right away, then one every FadeEvery.</summary>
    void Fade(ToastWindow toast, double from, double to, bool close)
    {
        var step = to > from ? FadeStep : -FadeStep;
        var value = from;
        var timer = new DispatcherTimer { Interval = FadeEvery };
        bool Next()
        {
            value += step;
            toast.Alpha = Math.Clamp(value, 0, 1);
            if (!(step > 0 ? value >= to : value <= to))
                return false;
            if (close)
            {
                windows.Remove(toast);
                toast.Close();
            }
            return true;
        }
        timer.Tick += (_, _) =>
        {
            if (Next())
                timer.Stop();
        };
        if (!Next())
            timer.Start();
    }

    /// <summary>A Tk label on a window in the accent color. The label has a two pixel border inside
    /// its padding; the accent shows three pixels on the left and one elsewhere.</summary>
    sealed class ToastWindow : Window
    {
        const double Stripe = 3, Edge = 1, Border = 2, PadX = 14, PadY = 9, TextSize = 13;
        static readonly Brush Face = Frozen(Color.FromRgb(0x2b, 0x2b, 0x2b));

        sealed class Surface(ToastWindow toast) : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc) => toast.Draw(dc);
        }

        readonly TkText.Run text;
        readonly IntPtr handle;
        readonly int stripe, edge, inset, labelW, labelH, line;

        public double Ppd { get; }
        public int PixelWidth => stripe + labelW + edge;
        public int PixelHeight => labelH + 2 * edge;

        public double Alpha
        {
            set => Native.SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(value * 255), Native.LwaAlpha);
        }

        public ToastWindow(string label, Color accent, Color foreground, double ppd)
        {
            Ppd = ppd;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Frozen(accent);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
            Content = new Surface(this);
            text = TkText.Format(label, TextSize, false, Frozen(foreground), ppd);
            stripe = Px(Stripe);
            edge = Px(Edge);
            inset = Px(Border + PadX);
            line = TkText.LineHeight(TextSize, ppd);
            labelW = text.Pixels + 2 * inset;
            labelH = line + 2 * Px(Border + PadY);
            handle = new WindowInteropHelper(this).EnsureHandle();
            var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64()
                        | Native.WsExToolWindow | Native.WsExNoActivate | Native.WsExLayered;
            Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style));
            Alpha = 0;
        }

        int Px(double dip) => (int)Math.Round(dip * Ppd);

        public void Move(int x, int y) =>
            Native.SetWindowPos(handle, IntPtr.Zero, x, y, PixelWidth, PixelHeight, Native.SwpNoZOrder | Native.SwpNoActivate);

        void Draw(DrawingContext dc)
        {
            dc.DrawRectangle(Face, null, new Rect(stripe / Ppd, edge / Ppd, labelW / Ppd, labelH / Ppd));
            // Tk puts the top of the line where the padding ends
            var top = edge + Px(Border + PadY);
            TkText.Draw(dc, text, (stripe + inset) / Ppd, (top + line / 2) / Ppd, TextSize, TkText.Anchor.W, Ppd);
        }

        static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
