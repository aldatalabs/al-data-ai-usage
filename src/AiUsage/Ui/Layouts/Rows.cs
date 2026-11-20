using System.Collections.Generic;
using System.Windows.Media;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Ui.Layouts;

/// <summary>Label, bar, percentage and timer on one row.</summary>
public sealed class Rows(LayoutOptions o) : Layout(o)
{
    double labelCol, pctCol, resetCol;

    double BarWidth => S(ItemCell * icons);

    protected override (double W, double H) CellSize(IReadOnlyList<Gauge> gauges)
    {
        double gap = S(8);
        var width = LabelWidth(gauges) + gap + BarWidth + gap + PctWidth(gauges) + DotWidth;
        var resetW = ResetWidth(gauges);
        if (resetW > 0)
            width += gap + resetW;
        return (width, S(13));
    }

    /// <summary>The columns are the same for every cell, so they are computed once per draw.</summary>
    public override void Draw(DrawingContext dc, IReadOnlyList<Gauge> gauges)
    {
        labelCol = LabelWidth(gauges);
        pctCol = PctWidth(gauges);
        resetCol = ResetWidth(gauges);
        base.Draw(dc, gauges);
    }

    protected override void DrawCell(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        double gap = S(8), barH = S(6);
        var cy = y + h / 2;
        TextW(dc, Label(gauge), x, cy, norm, theme.Dim);
        var xBar = x + labelCol + gap;
        Bar(dc, xBar, cy - barH / 2, BarWidth, barH, gauge);
        var xPct = xBar + BarWidth + gap + pctCol;
        TextE(dc, PctText(gauge), xPct, cy, bold, theme.Text);
        StatusDot(dc, xPct + S(DotPad), cy, gauge);
        if (resetCol > 0)
            TextW(dc, ResetText(gauge), xPct + DotWidth + gap, cy, small, theme.Dim);
    }
}

/// <summary>Label and percentage on top, the bar below, the timer at the bottom.</summary>
public sealed class Cells(LayoutOptions o) : Layout(o)
{
    public override int DefaultLines => 1;

    protected override (double W, double H) CellSize(IReadOnlyList<Gauge> gauges)
    {
        double gap = S(8), widest = S(64);
        var until = Measure($"{i18n.T("tip.until")} 00d00h", small);
        foreach (var gauge in gauges)
        {
            var line = Measure(Label(gauge), norm) + gap + Measure(PctText(gauge), bold) + DotWidth;
            widest = System.Math.Max(widest, System.Math.Max(line, until));
        }
        double height = S(12) + S(4) + S(6);
        if (showReset)
            height += S(3) + S(11);
        return (widest, height);
    }

    protected override void DrawCell(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        TextW(dc, Label(gauge), x, y + S(6), norm, theme.Dim);
        TextE(dc, PctText(gauge), x + w - DotWidth, y + S(6), bold, theme.Text);
        StatusDot(dc, x + w - S(6), y + S(6), gauge);
        double barH = S(6);
        var yBar = y + S(12) + S(4);
        Bar(dc, x, yBar, w, barH, gauge);
        if (showReset && ResetText(gauge) is { Length: > 0 } text)
            TextW(dc, $"{i18n.T("tip.until")} {text}", x, yBar + barH + S(3) + S(5), small, theme.Dim);
    }
}

/// <summary>The lowest variant: everything on one row in a small font.</summary>
public sealed class Strip(LayoutOptions o) : Layout(o)
{
    public override int DefaultLines => 1;

    protected override (double W, double H) CellSize(IReadOnlyList<Gauge> gauges)
    {
        double gap = S(5), widest = 0;
        foreach (var gauge in gauges)
        {
            var width = Measure(Label(gauge), small) + gap + S(28) + gap + Measure(PctText(gauge), bold) + DotWidth;
            if (showReset)
                width += gap + Measure(ResetText(gauge) is { Length: > 0 } t ? t : "00d00h", small);
            widest = System.Math.Max(widest, width);
        }
        return (widest, S(16));
    }

    protected override void DrawCell(DrawingContext dc, double x, double y, double w, double h, Gauge gauge)
    {
        double gap = S(5), barW = S(28), barH = S(6);
        var cy = y + h / 2;
        TextW(dc, Label(gauge), x, cy, small, theme.Dim);
        x += Measure(Label(gauge), small) + gap;
        Bar(dc, x, cy - barH / 2, barW, barH, gauge);
        x += barW + gap;
        TextW(dc, PctText(gauge), x, cy, bold, theme.Text);
        x += Measure(PctText(gauge), bold) + S(4);
        StatusDot(dc, x, cy, gauge);
        if (showReset)
            TextW(dc, ResetText(gauge), x + DotWidth, cy, small, theme.Dim);
    }
}
