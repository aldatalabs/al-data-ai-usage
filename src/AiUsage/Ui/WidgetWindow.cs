using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AlData.AiUsage.Ui;

/// <summary>The borderless widget window. Content is painted by a callback; position and size are
/// physical pixels, as config.json keeps them.</summary>
internal sealed class WidgetWindow : Window
{
    sealed class Surface : FrameworkElement
    {
        public Action<DrawingContext>? Paint;

        protected override void OnRender(DrawingContext dc) => Paint?.Invoke(dc);
    }

    readonly Surface surface = new();
    (int X, int Y)? drag;

    /// <summary>Double click.</summary>
    public event Action? DoubleClicked;
    /// <summary>Right click, at the cursor.</summary>
    public event Action? MenuRequested;
    /// <summary>A drag finished; the argument is the new top-left corner.</summary>
    public event Action<int, int>? Moved;
    public Func<bool> Locked { get; set; } = () => false;

    public IntPtr Handle { get; }

    /// <summary>Whatever is behind the window shows through its background. Fixed for the life
    /// of the window: WPF takes AllowsTransparency only before the handle exists.</summary>
    public bool Transparent { get; }

    public WidgetWindow(bool transparent = false)
    {
        Transparent = transparent;
        Title = "al-data-ai-usage";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        if (transparent)
        {
            AllowsTransparency = true;
            // ClearType needs to know the color behind each pixel; here nobody does.
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        }
        else
        {
            // Tk draws with ClearType; WPF falls back to grayscale unless asked.
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        }
        Content = surface;
        Handle = new WindowInteropHelper(this).EnsureHandle();
        // Out of Alt+Tab and the taskbar, like the Tk window it replaces.
        var style = Native.GetWindowLongPtr(Handle, Native.GwlExStyle).ToInt64();
        style = (style | Native.WsExToolWindow) & ~Native.WsExAppWindow;
        Native.SetWindowLongPtr(Handle, Native.GwlExStyle, new IntPtr(style));
        MouseLeftButtonDown += OnLeftDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            // After the click is done: a popup opened inside its handler cannot take the mouse.
            Dispatcher.BeginInvoke(() => MenuRequested?.Invoke(), System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    public double PixelsPerDip
    {
        get
        {
            var dpi = Native.GetDpiForWindow(Handle);
            return dpi == 0 ? VisualTreeHelper.GetDpi(this).PixelsPerDip : dpi / 96.0;
        }
    }

    /// <summary>A transparent window keeps the faintest trace of the background: Windows sends
    /// the mouse through pixels that are fully clear, and the widget could not be dragged.</summary>
    public void Paint(Color bg, Action<DrawingContext> paint)
    {
        Background = new SolidColorBrush(Transparent ? Color.FromArgb(1, bg.R, bg.G, bg.B) : bg);
        surface.Paint = paint;
        surface.InvalidateVisual();
    }

    /// <summary>One line of dim text: loading, or the first error. Returns its size in DIPs.</summary>
    public Size PaintStatus(string text, Color bg, Color dim)
    {
        var ft = TkText.Format(text, 11, false, new SolidColorBrush(dim), PixelsPerDip);
        Paint(bg, dc => TkText.Draw(dc, ft, 10, 12, 11, TkText.Anchor.W, PixelsPerDip));
        return new Size(ft.Width + 20, 24);
    }

    /// <summary>Physical pixels, no activation, z-order untouched.</summary>
    public void Place(int x, int y, int width, int height) =>
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, Native.SwpNoZOrder | Native.SwpNoActivate);

    public void KeepOnTop() =>
        Native.SetWindowPos(Handle, Native.HwndTopmost, 0, 0, 0, 0,
                            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            DoubleClicked?.Invoke();
            return;
        }
        if (Locked() || !Native.GetCursorPos(out var cursor) || !Native.GetWindowRect(Handle, out var rect))
            return;
        drag = (cursor.X - rect.Left, cursor.Y - rect.Top);
        CaptureMouse();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (drag is not { } offset || !Native.GetCursorPos(out var cursor))
            return;
        Native.SetWindowPos(Handle, IntPtr.Zero, cursor.X - offset.X, cursor.Y - offset.Y, 0, 0,
                            Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate);
    }

    void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (drag is null)
            return;
        drag = null;
        ReleaseMouseCapture();
        if (Native.GetWindowRect(Handle, out var rect))
            Moved?.Invoke(rect.Left, rect.Top);
    }
}
