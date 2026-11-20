using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AlData.AiUsage.Ui;

/// <summary>Windows 11 style context menu, drawn by hand as the Tk prototype drew it: WPF's
/// ContextMenu looks like neither. One borderless window per level, rounded by DWM; row
/// highlight, a column for check marks, side flyouts, Segoe UI. The sizes are the Tk prototype's
/// in DIPs, and text and shapes land on the pixels Tk puts them on.
///
/// The root window holds the mouse capture for the whole chain, as Tk's grab does: every event
/// arrives there and goes to the deepest menu under the pointer.</summary>
internal sealed class Menu
{
    const double ItemH = 30, SepH = 9, HeaderH = 26, PadV = 6;
    /// <summary>Gap between the highlight and the window edge.</summary>
    const double Side = 4;
    /// <summary>The column for the check mark or dot; in menus with icons, an icon column and a
    /// check mark column.</summary>
    const double Gutter = 34, GutterIcon = 52;
    /// <summary>Icon center from the left edge.</summary>
    const double IconX = 15;
    const double PadRight = 22;
    /// <summary>Room for the "›" on a row with a flyout.</summary>
    const double ArrowW = 16;
    /// <summary>Gap between the label and a hint at the right edge (version, shortcut).</summary>
    const double HintGap = 24;
    /// <summary>Row highlight corner radius.</summary>
    const double Radius = 5;
    const double MinWidth = 190;
    /// <summary>The Tk prototype measured a Tk label: its border and padding come on top of the text.</summary>
    const double LabelPad = 6;
    const double FontSize = 14, SmallSize = 12, HintSize = 10;
    /// <summary>Tolerance in pixels at the seam between windows, so the pointer does not fall into nowhere.</summary>
    const int EdgeSlack = 3;
    /// <summary>Like Windows: a flyout opens after a short delay.</summary>
    static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(300);

    static readonly Color HoverColor = Rgb(0x3a3a3a), SeparatorColor = Rgb(0x3c3c3c), AccentColor = Rgb(0xe8e8e8);
    /// <summary>Segoe UI has no check mark. Tk takes it from MS Gothic, whose embedded bitmap
    /// WPF cannot draw, so GDI draws it.</summary>
    const string CheckFace = "MS Gothic";
    static readonly Brush Bg = Solid(0x2b2b2b), Border = Solid(0x404040), Text = Solid(0xf0f0f0),
        Muted = Solid(0x9a9a9a), Disabled = Solid(0x777777), Accent = Solid(0xe8e8e8), Hover = Solid(0x3a3a3a);
    /// <summary>The COLORREF of the border DWM draws around the rounded window.</summary>
    const int DwmBorder = 0x00404040;

    enum Kind { Command, Check, Radio, Header, Separator, Submenu }

    sealed class Item
    {
        public required Kind Kind { get; init; }
        public string Label { get; init; } = "";
        public Action? Command { get; init; }
        public bool Enabled { get; init; } = true;
        public string? Icon { get; init; }
        public string? Hint { get; init; }
        public bool Checked { get; init; }
        public Menu? Child { get; set; }
    }

    readonly Func<Menu>? rebuild;
    Menu? parent;
    List<Item> items = [];
    MenuWindow? window;
    /// <summary>The window on screen and the monitor it is kept on, in physical pixels.</summary>
    Native.Rect box, screen;
    double ppd = 1;
    int pixelW, pixelH;
    /// <summary>The top of each item in DIPs, from the canvas inside the border.</summary>
    double[] tops = [];
    bool icons;
    double gutter = Gutter;
    int? hover, childIndex;
    Menu? child;
    DispatcherTimer? openTimer, switchTimer;
    /// <summary>Which side this flyout opened on relative to its parent.</summary>
    bool flippedLeft;

    /// <summary>rebuild returns a fresh copy of this menu. With it, a check or radio click leaves the
    /// menu open and redraws it from the new state; without it, any click closes.</summary>
    public Menu(Func<Menu>? rebuild = null) => this.rebuild = rebuild;

    public bool IsOpen => window is not null;

    // --- building --------------------------------------------------------
    /// <summary>hint: small muted text at the right edge of the row, like a shortcut.</summary>
    public void AddCommand(string label, Action? command, bool enabled = true, string? icon = null, string? hint = null) =>
        items.Add(new Item { Kind = Kind.Command, Label = label, Command = command, Enabled = enabled, Icon = icon, Hint = hint });

    public void AddCheck(string label, bool isChecked, Action command, string? icon = null) =>
        items.Add(new Item { Kind = Kind.Check, Label = label, Checked = isChecked, Command = command, Icon = icon });

    public void AddRadio(string label, bool selected, Action command) =>
        items.Add(new Item { Kind = Kind.Radio, Label = label, Checked = selected, Command = command });

    public void AddHeader(string label) => items.Add(new Item { Kind = Kind.Header, Label = label, Enabled = false });

    public void AddSeparator() => items.Add(new Item { Kind = Kind.Separator, Enabled = false });

    /// <summary>Returns an empty submenu; it is filled with the same Add calls.</summary>
    public Menu AddSubmenu(string label, string? icon = null)
    {
        var sub = new Menu { parent = this };
        items.Add(new Item { Kind = Kind.Submenu, Label = label, Icon = icon, Child = sub });
        return sub;
    }

    // --- showing ---------------------------------------------------------
    /// <summary>The root menu at a point on screen, in physical pixels.</summary>
    public void Popup(int x, int y)
    {
        var monitor = Native.MonitorFromPoint(new Native.Point { X = x, Y = y }, Native.MonitorDefaultToNearest);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        screen = Native.GetMonitorInfo(monitor, ref info)
            ? info.Monitor
            : new Native.Rect { Right = Native.GetSystemMetrics(Native.SmCxScreen), Bottom = Native.GetSystemMetrics(Native.SmCyScreen) };
        ppd = Native.GetDpiForMonitor(monitor, Native.MdtEffectiveDpi, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        Open(x, y, null, false);
    }

    void Open(int x, int y, Native.Rect? avoid, bool preferLeft)
    {
        var root = parent is null;
        var opened = new MenuWindow(this, root);
        window = opened;
        opened.MouseMove += (_, _) => Top.OnMotion();
        opened.MouseDown += (_, _) => Top.OnPress();
        opened.MouseUp += (_, e) => Top.OnRelease(e.ChangedButton);
        Measure();
        Place(x, y, avoid, preferLeft);
        opened.Show();
        opened.Place(box);   // showing may have moved it to another monitor's DPI
        if (!root)
            return;
        opened.PreviewKeyDown += (_, e) => e.Handled = Deepest.OnKey(e.Key);
        opened.Deactivated += (_, _) => Close();
        opened.LostMouseCapture += (_, _) => OnLostCapture();
        Native.SetForegroundWindow(opened.Handle);
        opened.CaptureMouse();
    }

    void Place(int x, int y, Native.Rect? avoid, bool preferLeft)
    {
        if (avoid is not { } outer)
        {
            // the root menu simply opens away from the cursor
            if (x + pixelW > screen.Right)
                x = Math.Max(screen.Left, x - pixelW);
            if (y + pixelH > screen.Bottom)
                y = Math.Max(screen.Top, y - pixelH);
        }
        else
        {
            // a flyout must not land on its parent or on any menu higher up the chain
            var toLeft = outer.Left - pixelW - 1;
            if (preferLeft && toLeft >= screen.Left)
                x = toLeft;   // the parent went left: keep the direction
            else if (x + pixelW > screen.Right)
                x = toLeft;
            flippedLeft = x < outer.Left;
            x = Math.Max(screen.Left, Math.Min(x, screen.Right - pixelW));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - pixelH));
        }
        box = new Native.Rect { Left = x, Top = y, Right = x + pixelW, Bottom = y + pixelH };
        window?.Place(box);
    }

    /// <summary>State changed behind an open menu (a background job finished): redraw it.</summary>
    public void Refresh() => Top.Reload();

    /// <summary>After a check or radio click: rebuild from the new state, keep the open flyouts.</summary>
    void Reload()
    {
        if (window is not null && rebuild is not null)
            Adopt(rebuild());
    }

    /// <summary>Take the items of a fresh copy into the open window. Flyouts are matched by row: the
    /// same row still a submenu keeps its flyout open. Labels may change length (another language),
    /// so the window is measured again.</summary>
    void Adopt(Menu fresh)
    {
        CancelOpen();
        CancelSwitch();
        items = fresh.items;
        foreach (var item in items)
            if (item.Child is { } sub)
                sub.parent = this;
        if (hover is { } h && (h >= items.Count || !items[h].Enabled))
            hover = null;
        Measure();
        if (parent is { childIndex: { } index } owner)
        {
            var (x, y) = owner.FlyoutSpot(index);
            Place(x, y, owner.box, owner.flippedLeft);
        }
        else
        {
            // stay put, only keep the grown window on screen
            Place(Math.Max(screen.Left, Math.Min(box.Left, screen.Right - pixelW)),
                  Math.Max(screen.Top, Math.Min(box.Top, screen.Bottom - pixelH)), null, false);
        }
        window?.Redraw();
        if (child is null || childIndex is not { } open)
            return;
        if (open < items.Count && items[open].Child is { } freshChild)
        {
            items[open].Child = child;
            child.Adopt(freshChild);
        }
        else
        {
            CloseChild();
        }
    }

    public void Close()
    {
        CancelOpen();
        CancelSwitch();
        CloseChild();
        hover = null;
        if (window is not { } closing)
            return;
        window = null;
        if (parent is null)
            closing.ReleaseMouseCapture();
        closing.Close();
    }

    Menu Top => parent?.Top ?? this;

    Menu Deepest => child?.Deepest ?? this;

    IEnumerable<Menu> Chain()
    {
        for (Menu? node = Top; node is not null; node = node.child)
            yield return node;
    }

    /// <summary>Another window took the mouse. While the menu is still the active window it takes
    /// it back; otherwise it could no longer see a click outside, so it closes.</summary>
    void OnLostCapture()
    {
        window?.Dispatcher.BeginInvoke(() =>
        {
            if (window is null || (window.IsActive && window.CaptureMouse()))
                return;
            Close();
        }, DispatcherPriority.Input);
    }

    /// <summary>Which menu in the chain is under the pointer. Searched from the deepest: at the seam
    /// between windows the flyout wins, otherwise the parent takes the boundary and the selection
    /// flickers.</summary>
    static Menu? Locate(Menu top, Native.Point p) => top.Chain().Reverse().FirstOrDefault(m => m.Inside(p, EdgeSlack));

    // --- flyouts ---------------------------------------------------------
    void CancelOpen()
    {
        openTimer?.Stop();
        openTimer = null;
    }

    void CancelSwitch()
    {
        switchTimer?.Stop();
        switchTimer = null;
    }

    void CancelAncestors()
    {
        for (var node = parent; node is not null; node = node.parent)
            node.CancelSwitch();
    }

    static DispatcherTimer After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return timer;
    }

    /// <summary>The path to a flyout runs diagonally and brushes neighboring rows. So switching is
    /// deferred: if the pointer reaches the open submenu in that time, the switch is canceled and
    /// the flyout stays.</summary>
    void ScheduleSwitch(int? index)
    {
        CancelSwitch();
        switchTimer = After(OpenDelay, () =>
        {
            switchTimer = null;
            if (index is { } i && items[i].Kind == Kind.Submenu)
                OpenSubmenu(i);
            else
                CloseChild();
        });
    }

    void CloseChild()
    {
        CancelSwitch();
        child?.Close();
        child = null;
        childIndex = null;
    }

    void OpenSubmenu(int index, bool focus = false)
    {
        if (childIndex == index || items[index].Child is not { } sub)
            return;
        CloseChild();
        var (x, y) = FlyoutSpot(index);
        sub.ppd = ppd;
        sub.screen = screen;
        sub.Open(x, y, box, flippedLeft);
        child = sub;
        childIndex = index;
        if (focus)
            sub.Step(1);
    }

    /// <summary>Where the flyout of a row opens: level with the row, past this menu's right edge.</summary>
    (int X, int Y) FlyoutSpot(int index) => (box.Right + 1, box.Top + (int)Math.Round((tops[index] - PadV) * ppd));

    void OpenHovered(bool focus = false)
    {
        if (hover is { } i && items[i].Kind == Kind.Submenu)
            OpenSubmenu(i, focus);
    }

    void Escape()
    {
        if (parent is { } owner)
            owner.CloseChild();
        else
            Close();
    }

    // --- input -----------------------------------------------------------
    bool IsRow(int index) => items[index].Kind is not (Kind.Separator or Kind.Header);

    /// <summary>The margin covers the gap between adjacent windows so the highlight does not blink.</summary>
    bool Inside(Native.Point p, int margin) =>
        window is not null
        && p.X >= box.Left + 1 - margin && p.X <= box.Right - 1 + margin
        && p.Y >= box.Top + 1 - margin && p.Y <= box.Bottom - 1 + margin;

    int? RowAt(Native.Point p)
    {
        if (!Inside(p, 0))
            return null;
        var y = (p.Y - box.Top - 1) / ppd;
        for (var i = 0; i < items.Count; i++)
            if (IsRow(i) && tops[i] <= y && y < tops[i] + ItemH)
                return i;
        return null;
    }

    void SetHover(int? index)
    {
        if (index == hover)
            return;
        hover = index;
        window?.Redraw();
    }

    void OnMotion()
    {
        if (!Native.GetCursorPos(out var p))
            return;
        if (Locate(this, p) is { } target)
        {
            target.HoverMotion(p);
            return;
        }
        foreach (var node in Chain())
        {
            node.CancelOpen();
            node.CancelSwitch();
        }
        Deepest.SetHover(null);
    }

    void HoverMotion(Native.Point p)
    {
        CancelAncestors();   // the pointer reached this flyout: its ancestors leave it alone
        var index = RowAt(p);
        if (index is { } i && !items[i].Enabled)
            index = null;
        SetHover(index);
        CancelOpen();
        if (child is not null)
        {
            if (index == childIndex)
                CancelSwitch();
            else
                ScheduleSwitch(index);
            return;
        }
        if (index is { } row && items[row].Kind == Kind.Submenu)
            openTimer = After(OpenDelay, () =>
            {
                openTimer = null;
                OpenHovered();
            });
    }

    /// <summary>A press outside every menu closes them all.</summary>
    void OnPress()
    {
        if (Native.GetCursorPos(out var p) && Locate(this, p) is null)
            Close();
    }

    void OnRelease(MouseButton button)
    {
        if (button != MouseButton.Left || !Native.GetCursorPos(out var p))
            return;
        if (Locate(this, p) is { } target)
            target.Activate(target.RowAt(p));
        else
            Close();
    }

    bool OnKey(Key key)
    {
        switch (key)
        {
            case Key.Escape or Key.Left:
                Escape();
                return true;
            case Key.Up:
                Step(-1);
                return true;
            case Key.Down:
                Step(1);
                return true;
            case Key.Right:
                OpenHovered(focus: true);
                return true;
            case Key.Enter:
                Activate(hover);
                return true;
            default:
                return false;
        }
    }

    void Step(int delta)
    {
        var order = Enumerable.Range(0, items.Count).Where(i => IsRow(i) && items[i].Enabled).ToList();
        if (order.Count == 0)
            return;
        var position = hover is { } h && order.IndexOf(h) is var at and >= 0
            ? (at + delta + order.Count) % order.Count
            : delta > 0 ? 0 : order.Count - 1;
        SetHover(order[position]);
    }

    void Activate(int? index)
    {
        if (index is not { } i || !items[i].Enabled)
            return;
        var item = items[i];
        if (item.Kind == Kind.Submenu)
        {
            OpenSubmenu(i, focus: true);
            return;
        }
        if (item.Command is not { } command)
            return;
        var top = Top;
        if (item.Kind is Kind.Check or Kind.Radio && top.rebuild is not null)
        {
            command();   // a setting: stay open so the next one is a click away
            top.Reload();
            return;
        }
        top.Close();
        command();
    }

    // --- drawing ---------------------------------------------------------
    double TextWidth(string text, double size) => TkText.Format(text, size, false, Text, ppd).Width;

    /// <summary>The gutter is wider only where there really are icons: radio lists (skins,
    /// languages, intervals) stay narrow.</summary>
    void Measure()
    {
        icons = Icons.Family is not null && items.Any(item => item.Icon is not null);
        gutter = icons ? GutterIcon : Gutter;
        tops = new double[items.Count];
        double width = MinWidth, y = PadV;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            tops[i] = y;
            if (item.Kind == Kind.Separator)
            {
                y += SepH;
                continue;
            }
            y += item.Kind == Kind.Header ? HeaderH : ItemH;
            var extra = item.Kind == Kind.Submenu ? ArrowW : 0;
            if (item.Hint is { } hint)
                extra = TextWidth(hint, HintSize) + LabelPad + HintGap;
            width = Math.Max(width, gutter + TextWidth(item.Label, FontSize) + LabelPad + PadRight + extra);
        }
        pixelW = (int)Math.Round(width * ppd);
        pixelH = (int)Math.Round((y + PadV) * ppd);
    }

    void Draw(DrawingContext dc)
    {
        var device = 1 / ppd;
        // the canvas sits inside a one pixel border
        dc.DrawRectangle(Bg, null, new Rect(device, device, (pixelW - 2) * device, (pixelH - 2) * device));
        dc.PushTransform(new TranslateTransform(device, device));
        var width = (pixelW - 2) * device;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var top = tops[i];
            if (item.Kind == Kind.Separator)
            {
                var mid = top + SepH / 2;
                Line(dc, Side + 8, mid, width - Side - 8, mid, SeparatorColor);
                continue;
            }
            if (item.Kind == Kind.Header)
            {
                Label(dc, item.Label.ToUpperInvariant(), gutter - 10, top + HeaderH / 2, SmallSize, Muted, TkText.Anchor.W);
                continue;
            }
            var cy = top + ItemH / 2;
            if (hover == i && item.Enabled)
                Rounded(dc, Side, top + 1, width - Side, top + ItemH - 1, Radius);
            if (icons && item.Icon is { } icon)
                Label(dc, icon, IconX, cy, FontSize, item.Enabled ? Muted : Disabled, TkText.Anchor.Center, Icons.Family);
            var mark = gutter - 18 + (icons ? 3 : 0);
            if (item.Checked && item.Kind == Kind.Check)
                TkText.DrawFallback(dc, "✓", CheckFace, mark, cy, FontSize, AccentColor, TkText.Anchor.Center, ppd);
            else if (item.Checked)
                Label(dc, "●", mark, cy, FontSize, Accent, TkText.Anchor.Center);
            Label(dc, item.Label, gutter, cy, FontSize, item.Enabled ? Text : Disabled, TkText.Anchor.W);
            if (item.Kind == Kind.Submenu)
                Label(dc, "›", width - 14, cy, FontSize, Muted, TkText.Anchor.Center);
            if (item.Hint is { } hint)
                Label(dc, hint, width - PadRight + 6, cy, HintSize, Muted, TkText.Anchor.E);
        }
        dc.Pop();
    }

    void Label(DrawingContext dc, string text, double x, double cy, double size, Brush brush, TkText.Anchor anchor,
               FontFamily? family = null) =>
        TkText.Draw(dc, TkText.Format(text, size, false, brush, ppd, family), x, cy, size, anchor, ppd, family);

    /// <summary>The highlight: four Tk ovals for the corners and two rectangles between them.</summary>
    void Rounded(DrawingContext dc, double x0, double y0, double x1, double y1, double radius)
    {
        var d = radius * 2;
        Oval(dc, x0, y0, x0 + d, y0 + d);
        Oval(dc, x1 - d, y0, x1, y0 + d);
        Oval(dc, x0, y1 - d, x0 + d, y1);
        Oval(dc, x1 - d, y1 - d, x1, y1);
        Box(dc, x0 + radius, y0, x1 - radius, y1);
        Box(dc, x0, y0 + radius, x1, y1 - radius);
    }

    void Oval(DrawingContext dc, double x0, double y0, double x1, double y1)
    {
        var (left, right) = TkDraw.Span(x0 * ppd, x1 * ppd);
        var (top, bottom) = TkDraw.Span(y0 * ppd, y1 * ppd);
        Put(dc, TkDraw.FillOval(left, top, right, bottom, HoverColor));
    }

    void Box(DrawingContext dc, double x0, double y0, double x1, double y1)
    {
        var (left, right) = TkDraw.Span(x0 * ppd, x1 * ppd);
        var (top, bottom) = TkDraw.Span(y0 * ppd, y1 * ppd);
        dc.DrawRectangle(Hover, null, new Rect(left / ppd, top / ppd, (right - left) / ppd, (bottom - top) / ppd));
    }

    void Line(DrawingContext dc, double x0, double y0, double x1, double y1, Color color) =>
        Put(dc, TkDraw.Line(TkDraw.Round(x0 * ppd), TkDraw.Round(y0 * ppd), TkDraw.Round(x1 * ppd), TkDraw.Round(y1 * ppd),
                            (int)(ppd + 0.5), color));

    void Put(DrawingContext dc, TkDraw.Stamp? stamp)
    {
        if (stamp is { } s)
            dc.DrawImage(s.Image, new Rect(s.X / ppd, s.Y / ppd, s.Image.PixelWidth / ppd, s.Image.PixelHeight / ppd));
    }

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    static Brush Solid(int rgb)
    {
        var brush = new SolidColorBrush(Rgb(rgb));
        brush.Freeze();
        return brush;
    }

    /// <summary>One level of the menu on screen.</summary>
    sealed class MenuWindow : Window
    {
        sealed class Surface(Menu menu) : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc) => menu.Draw(dc);
        }

        readonly Surface surface;
        /// <summary>Windows 11 rounds the window itself; older versions get a clip region.</summary>
        readonly bool rounded;

        public IntPtr Handle { get; }

        public MenuWindow(Menu menu, bool root)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = root;
            Topmost = true;
            Background = Border;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
            surface = new Surface(menu);
            Content = surface;
            Handle = new WindowInteropHelper(this).EnsureHandle();
            var style = Native.GetWindowLongPtr(Handle, Native.GwlExStyle).ToInt64() | Native.WsExToolWindow;
            if (!root)
                style |= Native.WsExNoActivate;   // the root keeps the keyboard
            Native.SetWindowLongPtr(Handle, Native.GwlExStyle, new IntPtr(style));
            rounded = Native.DwmSetWindowAttribute(Handle, Native.DwmwaWindowCornerPreference, Native.DwmwcpRound, sizeof(int)) == 0;
            if (rounded)
                Native.DwmSetWindowAttribute(Handle, Native.DwmwaBorderColor, DwmBorder, sizeof(int));
        }

        public void Place(Native.Rect box)
        {
            int width = box.Right - box.Left, height = box.Bottom - box.Top;
            Native.SetWindowPos(Handle, IntPtr.Zero, box.Left, box.Top, width, height, Native.SwpNoZOrder | Native.SwpNoActivate);
            if (!rounded)
                Native.SetWindowRgn(Handle, Native.CreateRoundRectRgn(0, 0, width + 1, height + 1, 16, 16), true);
        }

        public void Redraw() => surface.InvalidateVisual();
    }
}
