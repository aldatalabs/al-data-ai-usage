using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Ui.Layouts;

/// <summary>A gauge made of identical icons: colored ones are spent, gray ones are not.
/// The shapes are rasterized the way Pillow does it (see Raster), so the icon on the
/// boundary is filled partially, exactly by the fraction spent, instead of jumping a whole piece.
/// Each one is drawn twice, gray and colored, and stitched along a vertical line.</summary>
public abstract class Items(LayoutOptions o) : Layout(o)
{
    const int Row = 20;
    /// <summary>Draw larger and downscale: the edges come out anti-aliased.</summary>
    const int Supersample = 3;

    readonly Dictionary<(Color, int), BitmapSource> cache = [];
    int? shift;
    double labelCol, pctCol, resetCol;

    double ItemWidth => S(ItemCell);
    /// <summary>The icon in device pixels, as Tk has it.</summary>
    int IconW => (int)Math.Round(ItemWidth / Device);
    int IconH => (int)Math.Round(S(Row) / Device);

    /// <summary>Draw the shape in a w x h rectangle with the given colors.</summary>
    protected abstract void Paint(Raster r, int w, int h, Color main, Color shade);

    protected virtual Color SlotColor(Color color, int slot) => color;

    // --- icons -----------------------------------------------------------
    Raster Layer(int w, int h, Color main, Color shade)
    {
        var image = new Raster(w, h);
        Paint(image, w, h, main, shade);
        return image;
    }

    /// <summary>Each skin draws its shape in its own part of the rectangle: an arc at the top, a
    /// cup at the bottom. To keep the row of icons on one line with the text, the ink itself is
    /// centered, not the rectangle.</summary>
    int InkShift(int w, int h)
    {
        shift ??= Layer(w, h, Colors.White, Colors.White).InkRows() is { } rows
            ? (int)Math.Round(h / 2.0 - (rows.Top + rows.Bottom) / 2.0, MidpointRounding.ToEven)
            : 0;
        return shift.Value;
    }

    /// <summary>An icon filled to fraction from the left; the rest is in the track color.</summary>
    BitmapSource Icon(Color main, double fraction)
    {
        var share = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, fraction)) * 100, MidpointRounding.ToEven);
        if (cache.TryGetValue((main, share), out var cached))
            return cached;
        int w = IconW, h = IconH, bigW = w * Supersample, bigH = h * Supersample;
        var image = Layer(bigW, bigH, theme.Track, Theme.Mix(theme.Track, theme.Bg, 0.45));
        if (share > 0)
        {
            var full = Layer(bigW, bigH, main, Theme.Mix(main, theme.Bg, 0.45));
            image = share >= 100 ? full : Raster.Stitch(full, image, bigW * share / 100.0);
        }
        if (InkShift(bigW, bigH) is var dy and not 0)
            image = image.Shifted(dy);
        var small = image.Resized(w, h);
        var icon = transparent ? small.Clear(96 / Device) : small.Over(theme.Bg, 96 / Device);
        cache[(main, share)] = icon;
        return icon;
    }

    // --- layout ----------------------------------------------------------
    protected override (double W, double H) CellSize(IReadOnlyList<Gauge> gauges)
    {
        double gap = S(8);
        var width = LabelWidth(gauges) + gap + ItemWidth * icons + gap + PctWidth(gauges) + DotWidth;
        var resetW = ResetWidth(gauges);
        if (resetW > 0)
            width += gap + resetW;
        return (width, S(Row));
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
        double gap = S(8);
        var cy = y + h / 2;
        var itemsW = ItemWidth * icons;
        var xItems = x + labelCol + gap;
        TextW(dc, Label(gauge), x, cy, norm, theme.Dim);
        var filled = (gauge.Value ?? 0) * icons;
        var color = ColorOf(gauge);
        // Tk centers an image on cy with whole pixels: half the height rounds down.
        var top = Px(cy) - IconH / 2 * Device;
        for (var slot = 0; slot < icons; slot++)
        {
            var icon = Icon(SlotColor(color, slot), filled - slot);
            dc.DrawImage(icon, new Rect(Px(xItems + ItemWidth * slot), top, IconW * Device, IconH * Device));
        }
        if (showPace && Model.Pace(gauge) is { } forecast)
        {
            var tick = Px(xItems + itemsW * forecast.Elapsed) + Device / 2;
            dc.DrawLine(Pen(theme.TickColor, Device), new Point(tick, Px(cy - S(7))), new Point(tick, Px(cy + S(7))));
        }
        var xPct = xItems + itemsW + gap + pctCol;
        TextE(dc, PctText(gauge), xPct, cy, bold, theme.Text);
        StatusDot(dc, xPct + S(DotPad), cy, gauge);
        if (resetCol > 0)
            TextW(dc, ResetText(gauge), xPct + DotWidth + gap, cy, small, theme.Dim);
    }
}

public sealed class Hearts(LayoutOptions o) : Items(o)
{
    /// <summary>Segoe UI Symbol, for the ♥; without it the hearts stay empty.</summary>
    static readonly Lazy<GlyphTypeface?> Face = new(() =>
    {
        try
        {
            return new GlyphTypeface(new Uri(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisym.ttf")));
        }
        catch (Exception)
        {
            return null;
        }
    });

    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        if (Face.Value is { } face)
            r.Glyph(face, '♥', (int)(h * 0.78), w / 2.0, h / 2.0, main);
    }
}

/// <summary>Lollipop: a ball on a stick with a highlight.</summary>
public sealed class Candies(LayoutOptions o) : Items(o)
{
    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        var radius = w * 0.42;
        double cx = w / 2.0, cy = h * 0.38;
        r.Ellipse(cx - radius, cy - radius, cx + radius, cy + radius, main);
        // a highlight instead of a swirl: at this size the swirl read as a "?"
        var spot = radius * 0.3;
        r.Ellipse(cx - radius * 0.45 - spot, cy - radius * 0.45 - spot,
                  cx - radius * 0.45 + spot, cy - radius * 0.45 + spot, shade);
        r.Line(cx, cy + radius * 0.8, cx, h * 0.92, shade, Math.Max(1, (int)(w * 0.1)));
    }
}

/// <summary>Cigarette: a stick with a filter.</summary>
public sealed class Smokes(LayoutOptions o) : Items(o)
{
    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        var half = w * 0.19;
        var cx = w / 2.0;
        double top = h * 0.1, bottom = h * 0.9;
        var split = bottom - (bottom - top) * 0.3;
        r.Rectangle(cx - half, top, cx + half, split, main);
        r.Rectangle(cx - half, split, cx + half, bottom, shade);
    }
}

/// <summary>Cup: a cone with a handle.</summary>
public sealed class Coffee(LayoutOptions o) : Items(o)
{
    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        double left = w * 0.22, right = w * 0.78;
        double top = h * 0.22, bottom = h * 0.72;
        r.Polygon(main, (left, top), (right, top), (right - w * 0.1, bottom), (left + w * 0.1, bottom));
        r.Arc(right - w * 0.12, top + h * 0.04, right + w * 0.22, bottom - h * 0.04,
              -80, 80, shade, Math.Max(1, (int)(w * 0.09)));
        r.Line(left - w * 0.05, bottom + h * 0.1, right + w * 0.05, bottom + h * 0.1,
               shade, Math.Max(1, (int)(h * 0.05)));
    }
}

/// <summary>Glass: a triangular bowl, a stem, a base.</summary>
public sealed class Cocktails(LayoutOptions o) : Items(o)
{
    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        double left = w * 0.16, right = w * 0.84;
        double top = h * 0.18, mid = h * 0.58, bottom = h * 0.88;
        r.Polygon(main, (left, top), (right, top), (w / 2.0, mid));
        r.Line(w / 2.0, mid, w / 2.0, bottom, shade, Math.Max(1, (int)(w * 0.1)));
        r.Line(w * 0.3, bottom, w * 0.7, bottom, shade, Math.Max(1, (int)(h * 0.06)));
    }
}

/// <summary>Sunflower: petals in a circle and a center.</summary>
public sealed class Flowers(LayoutOptions o) : Items(o)
{
    const int Petals = 6;

    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        double cx = w / 2.0, cy = h / 2.0;
        double ring = w * 0.3, petal = w * 0.2;
        for (var index = 0; index < Petals; index++)
        {
            var angle = (double)(360 * index) / Petals * (Math.PI / 180.0);
            double px = cx + ring * Math.Cos(angle), py = cy - ring * Math.Sin(angle);
            r.Ellipse(px - petal, py - petal, px + petal, py + petal, main);
        }
        var core = w * 0.17;
        r.Ellipse(cx - core, cy - core, cx + core, cy + core, shade);
    }
}

/// <summary>Rainbow: each arc in its own spectrum color.</summary>
public sealed class Rainbow(LayoutOptions o) : Items(o)
{
    /// <summary>From green to red: the first icons are "safe", the last ones are near the ceiling.</summary>
    static readonly Color[] Spectrum =
    [
        Color.FromRgb(0x4a, 0xde, 0x80),
        Color.FromRgb(0xb9, 0xe7, 0x69),
        Color.FromRgb(0xff, 0xd1, 0x66),
        Color.FromRgb(0xff, 0x8c, 0x42),
        Color.FromRgb(0xff, 0x5f, 0x6d),
    ];

    /// <summary>The spectrum stretched over any number of icons.</summary>
    protected override Color SlotColor(Color color, int slot)
    {
        if (icons < 2)
            return Spectrum[0];
        var pos = (double)slot / (icons - 1) * (Spectrum.Length - 1);
        var low = Math.Min((int)pos, Spectrum.Length - 2);
        return Theme.Mix(Spectrum[low], Spectrum[low + 1], pos - low);
    }

    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        var inset = w * 0.1;
        r.Arc(inset, h * 0.22, w - inset, h * 1.0, 180, 360, main, Math.Max(1, (int)(w * 0.24)));
    }
}

/// <summary>A striped horn.</summary>
public sealed class Unicorns(LayoutOptions o) : Items(o)
{
    protected override void Paint(Raster r, int w, int h, Color main, Color shade)
    {
        double top = h * 0.08, bottom = h * 0.92;
        var half = w * 0.32;
        r.Polygon(main, (w / 2.0, top), (w / 2.0 - half, bottom), (w / 2.0 + half, bottom));
        foreach (var part in (double[])[0.42, 0.72])
        {
            var y = top + (bottom - top) * part;
            var spread = half * part;
            r.Line(w / 2.0 - spread, y, w / 2.0 + spread, y, shade, Math.Max(1, (int)(h * 0.05)));
        }
    }
}
