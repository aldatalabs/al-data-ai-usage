using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Ui.Layouts;

/// <summary>What a skin needs from the config, read once per rebuild.</summary>
public sealed record LayoutOptions(Theme Theme, double Scale, bool ShowReset, I18n I18n, bool ShowPace,
                                   string ColorMode, int Icons, JsonNode? Lines, double PixelsPerDip,
                                   bool Transparent = false);

/// <summary>A font the skins draw with: em size in DIPs, regular or bold Segoe UI.</summary>
public readonly record struct Font(double Size, bool Bold);

/// <summary>A skin describes one cell, what a single gauge looks like. Laying the cells out on a
/// grid is the base class's job: Lines sets how many lines to use, the rest is computed.
/// Units are DIPs, which are the Tk prototype's pixels at 100% scaling.</summary>
public abstract class Layout
{
    /// <summary>Gap between the % and the dot.</summary>
    protected const int DotPad = 5;
    /// <summary>The width of one icon in the icon skins; Rows sizes its bar by it.</summary>
    public const int ItemCell = 16;

    readonly Dictionary<Color, SolidColorBrush> brushes = [];
    readonly Dictionary<(Color, double), Pen> pens = [];
    protected readonly Theme theme;
    protected readonly I18n i18n;
    protected readonly bool showPace;
    protected readonly string colorMode;
    protected readonly int icons;
    protected readonly int lines;
    protected readonly bool showReset;
    /// <summary>The window has no background of its own: nothing may be blended into theme.Bg.</summary>
    protected readonly bool transparent;
    readonly double scale;
    readonly double pixelsPerDip;
    protected readonly Font small, norm, bold, tiny;

    public virtual bool SupportsReset => true;
    /// <summary>The skin's natural layout when lines is "auto".</summary>
    public virtual int DefaultLines => 3;

    protected Layout(LayoutOptions o)
    {
        theme = o.Theme;
        i18n = o.I18n;
        scale = o.Scale;
        showPace = o.ShowPace;
        colorMode = o.ColorMode;
        pixelsPerDip = o.PixelsPerDip;
        transparent = o.Transparent;
        icons = Math.Clamp(o.Icons, 3, 20);
        lines = Json.AsDouble(o.Lines) is { } n && !double.IsNaN(n) ? Math.Clamp((int)n, 1, 3) : DefaultLines;
        showReset = o.ShowReset && SupportsReset;
        small = new Font(S(9), false);
        norm = new Font(S(10), false);
        bold = new Font(S(10), true);
        tiny = new Font(S(8), false);
    }

    public int S(double value) => Math.Max(1, (int)Math.Round(value * scale, MidpointRounding.ToEven));

    // --- grid ------------------------------------------------------------
    /// <summary>How many lines and columns the current gauges take.</summary>
    (int Lines, int Cols) Grid(IReadOnlyList<Gauge> gauges)
    {
        var count = Math.Max(1, gauges.Count);
        var used = Math.Max(1, Math.Min(lines, count));
        return (used, (count + used - 1) / used);
    }

    protected abstract (double W, double H) CellSize(IReadOnlyList<Gauge> gauges);

    protected abstract void DrawCell(DrawingContext dc, double x, double y, double w, double h, Gauge gauge);

    /// <summary>Gauges grouped by source, order preserved.</summary>
    static List<(string Source, List<Gauge> Block)> Groups(IReadOnlyList<Gauge> gauges)
    {
        var groups = new List<(string, List<Gauge>)>();
        foreach (var gauge in gauges)
        {
            if (groups.Count > 0 && groups[^1].Item1 == gauge.Source)
                groups[^1].Item2.Add(gauge);
            else
                groups.Add((gauge.Source, [gauge]));
        }
        return groups;
    }

    int CaptionHeight => S(12);

    /// <summary>Groups get a caption only when there really are several sources.</summary>
    static bool Captioned(IReadOnlyList<Gauge> gauges) =>
        gauges.Where(g => g.Source != "").Select(g => g.Source).Distinct().Count() > 1;

    List<(string Source, List<Gauge> Block)> Blocks(IReadOnlyList<Gauge> gauges, bool captioned) =>
        captioned ? Groups(gauges) : [("", gauges.ToList())];

    public Size Size(IReadOnlyList<Gauge> gauges)
    {
        var (cellW, cellH) = CellSize(gauges);
        var captioned = Captioned(gauges);
        double width = 0, height = 0;
        foreach (var (_, block) in Blocks(gauges, captioned))
        {
            var (used, cols) = Grid(block);
            width = Math.Max(width, cellW * cols + S(12) * (cols - 1));
            height += cellH * used + (captioned ? CaptionHeight : 0);
        }
        return new Size(S(6) * 2 + width, S(5) * 2 + height);
    }

    public virtual void Draw(DrawingContext dc, IReadOnlyList<Gauge> gauges)
    {
        double padX = S(6), y = S(5);
        var (cellW, cellH) = CellSize(gauges);
        var captioned = Captioned(gauges);
        foreach (var (source, block) in Blocks(gauges, captioned))
        {
            if (captioned)
            {
                TextW(dc, source, padX, y + CaptionHeight / 2.0, small, theme.Dim);
                y += CaptionHeight;
            }
            var (used, cols) = Grid(block);
            for (var index = 0; index < block.Count; index++)
            {
                var (row, col) = Math.DivRem(index, cols);
                DrawCell(dc, padX + col * (cellW + S(12)), y + row * cellH, cellW, cellH, block[index]);
            }
            y += cellH * used;
        }
    }

    // --- shared helpers --------------------------------------------------
    /// <summary>usage: by the fraction spent; pace: by the projection at reset time.</summary>
    protected Color ColorOf(Gauge gauge)
    {
        if (colorMode == "pace" && Model.Pace(gauge) is { } forecast && theme.PaceLevels.Count > 0)
            return Model.LevelColor(forecast.Projected, theme.PaceLevels, theme.Dim);
        return Model.LevelColor(gauge.Value, theme.Levels, theme.Dim);
    }

    protected string Label(Gauge gauge) => i18n.GaugeLabel(gauge);

    protected static string PctText(Gauge gauge) => Model.Percent(gauge) is { } pct ? $"{pct}%" : "—";

    protected string ResetText(Gauge gauge) => showReset ? Model.Countdown(gauge.ResetsAt, i18n.Units) : "";

    protected double LabelWidth(IReadOnlyList<Gauge> gauges) => gauges.Select(g => Measure(Label(g), norm)).DefaultIfEmpty(0).Max();

    /// <summary>The % column is sized by the widest actual number: "3%" must not take the room of "100%".</summary>
    protected double PctWidth(IReadOnlyList<Gauge> gauges) => gauges.Select(g => Measure(PctText(g), bold)).DefaultIfEmpty(0).Max();

    protected double ResetWidth(IReadOnlyList<Gauge> gauges) =>
        showReset ? gauges.Select(ResetText).Append("00d00h").Max(t => Measure(t, small)) : 0;

    protected double DotWidth => S(DotPad) + S(6);

    /// <summary>Dot after the percentage, colored by the projection at reset time. Hollow while
    /// there is no projection yet: the window has just started and the pace is noise.</summary>
    protected void StatusDot(DrawingContext dc, double x, double cy, Gauge gauge)
    {
        double r = S(3);
        if (Model.Pace(gauge) is not { } forecast || theme.PaceLevels.Count == 0)
        {
            Oval(dc, x, cy - r, x + 2 * r, cy + r, null, theme.Dim, Device);
            return;
        }
        var color = Model.LevelColor(forecast.Projected, theme.PaceLevels, theme.Dim);
        Oval(dc, x, cy - r, x + 2 * r, cy + r, color, null);
    }

    /// <summary>A bar with rounded ends, horizontal or vertical: the radius follows the short side.
    /// Built the way Tk builds it: an oval at each end and a rectangle between them.</summary>
    protected void RoundRect(DrawingContext dc, double x0, double y0, double x1, double y1, Color color)
    {
        double width = x1 - x0, height = y1 - y0;
        if (width <= 0 || height <= 0)
            return;
        if (width >= height)
        {
            var r = height / 2;
            if (width <= 2 * r)
            {
                Oval(dc, x0, y0, x1, y1, color, null);
                return;
            }
            Oval(dc, x0, y0, x0 + 2 * r, y1, color, null);
            Oval(dc, x1 - 2 * r, y0, x1, y1, color, null);
            Box(dc, x0 + r, y0, x1 - r, y1, color);
            return;
        }
        var half = width / 2;
        Oval(dc, x0, y0, x1, y0 + 2 * half, color, null);
        Oval(dc, x0, y1 - 2 * half, x1, y1, color, null);
        Box(dc, x0, y0 + half, x1, y1 - half, color);
    }

    // --- Tk shapes -------------------------------------------------------
    /// <summary>Tk's oval, filled or outlined; the outline width is in DIPs.</summary>
    protected void Oval(DrawingContext dc, double x0, double y0, double x1, double y1, Color? fill, Color? outline,
                        double width = 0)
    {
        var (left, right) = TkDraw.Span(x0 * pixelsPerDip, x1 * pixelsPerDip);
        var (top, bottom) = TkDraw.Span(y0 * pixelsPerDip, y1 * pixelsPerDip);
        if (fill is { } color)
            Stamp(dc, TkDraw.FillOval(left, top, right, bottom, color));
        if (outline is { } line)
            Stamp(dc, TkDraw.OutlineOval(left, top, right, bottom, LineWidth(width), line));
    }

    /// <summary>Tk's arc with style=arc: degrees counterclockwise from three o'clock.</summary>
    protected void Arc(DrawingContext dc, double x0, double y0, double x1, double y1, double start, double extent,
                       Color color, double width) =>
        Stamp(dc, TkDraw.Arc(Dev(x0), Dev(y0), Dev(x1), Dev(y1), start, extent, LineWidth(width), color));

    /// <summary>Tk's line from one point to another.</summary>
    protected void Line(DrawingContext dc, double x0, double y0, double x1, double y1, Color color, double width) =>
        Stamp(dc, TkDraw.Line(Dev(x0), Dev(y0), Dev(x1), Dev(y1), LineWidth(width), color));

    /// <summary>Tk's rectangle: whole device pixels, the far edges excluded.</summary>
    void Box(DrawingContext dc, double x0, double y0, double x1, double y1, Color color)
    {
        var (left, right) = TkDraw.Span(x0 * pixelsPerDip, x1 * pixelsPerDip);
        var (top, bottom) = TkDraw.Span(y0 * pixelsPerDip, y1 * pixelsPerDip);
        dc.DrawRectangle(Brush(color), null, new Rect(left * Device, top * Device, (right - left) * Device, (bottom - top) * Device));
    }

    /// <summary>A shape GDI drew, put back on its device pixels.</summary>
    void Stamp(DrawingContext dc, TkDraw.Stamp? stamp)
    {
        if (stamp is { } s)
            dc.DrawImage(s.Image, new Rect(s.X * Device, s.Y * Device, s.Image.PixelWidth * Device, s.Image.PixelHeight * Device));
    }

    /// <summary>A DIP coordinate in device pixels, rounded as Tk rounds it.</summary>
    int Dev(double dip) => TkDraw.Round(dip * pixelsPerDip);

    /// <summary>Tk_ConfigOutlineGC: the width in whole device pixels.</summary>
    int LineWidth(double dip) => (int)(dip * pixelsPerDip + 0.5);

    protected void Bar(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        RoundRect(dc, x, y, x + w, y + h, theme.Track);
        if (gauge.Value is { } value && value != 0)
            RoundRect(dc, x, y, x + w * value, y + h, ColorOf(gauge));
        PaceTick(dc, x, y, w, h, gauge);
    }

    /// <summary>A tick where the fill would be if the window were spent evenly. Fill to the right of
    /// the tick means spending faster than the limit resets.</summary>
    protected void PaceTick(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        if (!showPace || Model.Pace(gauge) is not { } forecast)
            return;
        var tick = Px(x + w * forecast.Elapsed) + Device / 2;
        dc.DrawLine(Pen(theme.TickColor, Device), new Point(tick, Px(y - S(2))), new Point(tick, Px(y + h + S(2))));
    }

    /// <summary>One device pixel in DIPs: the width of a crisp 1 px line.</summary>
    protected double Device => 1 / pixelsPerDip;

    /// <summary>Tk's rounding of a coordinate: half up, to a whole device pixel.</summary>
    protected double Px(double dip) => TkText.Snap(dip, pixelsPerDip);

    // --- text ------------------------------------------------------------
    TkText.Run Format(string text, Font font, Color color) =>
        TkText.Format(text, font.Size, font.Bold, Brush(color), pixelsPerDip);

    protected double Measure(string text, Font font) =>
        text.Length == 0 ? 0 : Format(text, font, Colors.Black).Width;

    void Text(DrawingContext dc, string text, double x, double cy, Font font, Color color, TkText.Anchor anchor)
    {
        if (text.Length > 0)
            TkText.Draw(dc, Format(text, font, color), x, cy, font.Size, anchor, pixelsPerDip);
    }

    /// <summary>Text with its left edge at x, centered on cy.</summary>
    protected void TextW(DrawingContext dc, string text, double x, double cy, Font font, Color color) =>
        Text(dc, text, x, cy, font, color, TkText.Anchor.W);

    /// <summary>Text with its right edge at x, centered on cy.</summary>
    protected void TextE(DrawingContext dc, string text, double x, double cy, Font font, Color color) =>
        Text(dc, text, x, cy, font, color, TkText.Anchor.E);

    /// <summary>Text centered on (cx, cy).</summary>
    protected void TextC(DrawingContext dc, string text, double cx, double cy, Font font, Color color) =>
        Text(dc, text, cx, cy, font, color, TkText.Anchor.Center);

    protected SolidColorBrush Brush(Color color)
    {
        if (!brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes[color] = brush;
        }
        return brush;
    }

    protected Pen Pen(Color color, double width)
    {
        if (!pens.TryGetValue((color, width), out var pen))
        {
            pen = new Pen(Brush(color), width) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            pen.Freeze();
            pens[(color, width)] = pen;
        }
        return pen;
    }
}
