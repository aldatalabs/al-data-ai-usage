using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Ui.Layouts;

/// <summary>Vertical bars, like an equalizer.</summary>
public sealed class Bars(LayoutOptions o) : Layout(o)
{
    public override bool SupportsReset => false;
    public override int DefaultLines => 1;

    protected override (double W, double H) CellSize(IReadOnlyList<Gauge> gauges)
    {
        var widest = gauges.Select(g => Measure(Label(g), tiny)).DefaultIfEmpty(0).Max();
        return (Math.Max(S(16), widest + S(4)), S(38));
    }

    protected override void DrawCell(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        double barW = S(11);
        var left = x + (w - barW) / 2;
        double top = y + S(2), bottom = y + S(26);
        RoundRect(dc, left, top, left + barW, bottom, theme.Track);
        if (gauge.Value is { } value && value != 0)
            RoundRect(dc, left, bottom - (bottom - top) * value, left + barW, bottom, ColorOf(gauge));
        if (showPace && Model.Pace(gauge) is { } forecast)
        {
            var ty = Px(bottom - (bottom - top) * forecast.Elapsed) + Device / 2;
            dc.DrawLine(Pen(theme.TickColor, Device), new Point(Px(left - S(2)), ty), new Point(Px(left + barW + S(2)), ty));
        }
        TextC(dc, Label(gauge), x + w / 2, bottom + S(7), tiny, theme.Dim);
    }
}

/// <summary>Rings with the percentage inside.</summary>
public sealed class Rings(LayoutOptions o) : Layout(o)
{
    public override bool SupportsReset => false;
    public override int DefaultLines => 1;

    protected override (double W, double H) CellSize(IReadOnlyList<Gauge> gauges)
    {
        var widest = gauges.Select(g => Measure(Label(g), tiny)).DefaultIfEmpty(0).Max();
        return (Math.Max(S(32), widest + S(4)), S(42));
    }

    protected override void DrawCell(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        double radius = S(14), width = S(4);
        var cx = x + w / 2;
        var cy = y + S(2) + radius;
        double x0 = cx - radius, y0 = cy - radius, x1 = cx + radius, y1 = cy + radius;
        Oval(dc, x0, y0, x1, y1, null, theme.Track, width);
        if (gauge.Value is { } value && value != 0)
            Arc(dc, x0, y0, x1, y1, 90, -359.9 * Math.Clamp(value, 0, 1), ColorOf(gauge), width);
        if (showPace && Model.Pace(gauge) is { } forecast)
        {
            var angle = (90 - 360 * forecast.Elapsed) * Math.PI / 180;
            double inner = radius - width, outer = radius + S(2);
            Line(dc, cx + inner * Math.Cos(angle), cy - inner * Math.Sin(angle),
                 cx + outer * Math.Cos(angle), cy - outer * Math.Sin(angle), theme.TickColor, Device);
        }
        var pct = Model.Percent(gauge);
        TextC(dc, pct is null ? "—" : pct.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), cx, cy, bold, theme.Text);
        TextC(dc, Label(gauge), cx, cy + radius + S(6), tiny, theme.Dim);
    }
}

/// <summary>The skins by config name. Unknown names fall back to Rows.</summary>
public static class Skins
{
    public static readonly string[] Names =
        ["rows", "cells", "strip", "bars", "rings",
         "hearts", "candies", "smokes", "coffee", "cocktails", "flowers", "rainbow", "unicorns"];

    public static Layout Make(string name, LayoutOptions o) => name switch
    {
        "cells" => new Cells(o),
        "strip" => new Strip(o),
        "bars" => new Bars(o),
        "rings" => new Rings(o),
        "hearts" => new Hearts(o),
        "candies" => new Candies(o),
        "smokes" => new Smokes(o),
        "coffee" => new Coffee(o),
        "cocktails" => new Cocktails(o),
        "flowers" => new Flowers(o),
        "rainbow" => new Rainbow(o),
        "unicorns" => new Unicorns(o),
        _ => new Rows(o),
    };
}
