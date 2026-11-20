using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlData.AiUsage.Ui;

/// <summary>Canvas shapes drawn the way Tk 9 draws them on Windows: the same GDI calls with the
/// same arguments, into an offscreen bitmap. WPF's aliased geometry lands on other pixels for
/// anything larger than a dot, and its arcs differ everywhere. Coordinates are device pixels.</summary>
internal static class TkDraw
{
    /// <summary>A shape's pixels in its color, and where its top left goes.</summary>
    public sealed record Stamp(BitmapSource Image, int X, int Y);

    enum Kind { FillOval, OutlineOval, Arc, Line }

    readonly record struct Key(Kind Kind, int X1, int Y1, int X2, int Y2, int A, int B, int Width, Color Color);

    const uint White = 0x00FFFFFF;
    const int Turn = 64 * 360;
    /// <summary>Values move, so stamps pile up; past this many the cache starts over.</summary>
    const int CacheLimit = 512;

    static readonly Dictionary<Key, Stamp?> Cache = [];
    static readonly Dictionary<(string, string, int, Color), (Stamp?, int)> Texts = [];
    static readonly Dictionary<(string, string, int, bool), int> Widths = [];
    static readonly Dictionary<(string, int, bool), IntPtr> Fonts = [];
    static IntPtr measurer;

    /// <summary>Tk_CanvasDrawableCoords: half away from zero.</summary>
    public static int Round(double v) => (int)(v > 0 ? v + 0.5 : v - 0.5);

    /// <summary>The edges of an oval or a rectangle along one axis. A box thinner than a pixel
    /// gets the pixel it covers most, as DisplayRectOval does.</summary>
    public static (int Lo, int Hi) Span(double a, double b)
    {
        int lo = Round(a), hi = Round(b);
        if (hi != lo)
            return (lo, hi);
        int ia = (int)a, ib = (int)b;
        if (ia == ib)
            return ia > 0 ? (lo, hi + 1) : (lo - 1, hi);
        var pivot = ia > 0 ? ib : ia;
        return b - pivot > pivot - a ? (lo, hi + 1) : (lo - 1, hi);
    }

    public static Stamp? FillOval(int x1, int y1, int x2, int y2, Color color) =>
        Shape(new Key(Kind.FillOval, x1, y1, x2, y2, 0, Turn, 0, color));

    /// <summary>An oval's outline: Tk gives it projecting caps.</summary>
    public static Stamp? OutlineOval(int x1, int y1, int x2, int y2, int width, Color color) =>
        Shape(new Key(Kind.OutlineOval, x1, y1, x2, y2, 0, Turn, width, color));

    /// <summary>An arc item with style=arc: degrees counterclockwise from three o'clock.</summary>
    public static Stamp? Arc(int x1, int y1, int x2, int y2, double start, double extent, int width, Color color)
    {
        // ConfigureArc keeps both angles within a turn, DisplayArc makes them 64ths of a degree
        start -= (int)(start / 360.0) * 360.0;
        if (start < 0)
            start += 360.0;
        extent -= (int)(extent / 360.0) * 360.0;
        int a = (int)(64 * start + 0.5), b = (int)(64 * extent + 0.5);
        if (b == 0)
            return null;
        if (x2 <= x1)
            x2 = x1 + 1;
        if (y2 <= y1)
            y2 = y1 + 1;
        return Shape(new Key(Kind.Arc, x1, y1, x2, y2, a, b, width, color));
    }

    /// <summary>A two-point line item with butt caps.</summary>
    public static Stamp? Line(int x1, int y1, int x2, int y2, int width, Color color) =>
        Shape(new Key(Kind.Line, x1, y1, x2, y2, 0, 0, width, color));

    static Stamp? Shape(Key key)
    {
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        if (Cache.Count >= CacheLimit)
            Cache.Clear();
        var stamp = Render(key);
        Cache[key] = stamp;
        return stamp;
    }

    // --- GDI -------------------------------------------------------------
    /// <summary>SetUpGraphicsPort: a cosmetic pen below 2 px, a geometric one with Tk's caps above.</summary>
    static IntPtr Pen(int width, uint style)
    {
        if (width < 2)
            return Native.CreatePen(Native.PsSolid, width, White);
        var brush = new Native.LogBrush { Style = Native.BsSolid, Color = White };
        return Native.ExtCreatePen(Native.PsGeometric | Native.PsSolid | style, (uint)width, brush, 0, IntPtr.Zero);
    }

    /// <summary>tkWinDraw.c DrawOrFillArc: the X angles become the two radial points GDI wants,
    /// and the box grows by a pixel, since X and Windows count its far edges differently.</summary>
    static void ArcCall(IntPtr dc, int x, int y, int w, int h, int start, int extent, bool fill)
    {
        var clockwise = extent < 0;
        var full = extent >= Turn || extent <= -Turn;
        start %= Turn;
        if (start < 0)
            start += Turn;
        extent = (start + extent) % Turn;
        if (extent < 0)
            extent += Turn;
        if (clockwise)
            (start, extent) = (extent, start);
        double rs = (double)start / 64 * Math.PI / 180, re = (double)extent / 64 * Math.PI / 180;
        double xr = x + w / 2.0, yr = y + h / 2.0;
        int xs = (int)(xr + Math.Cos(rs) * w / 2.0 + 0.5), ys = (int)(yr + Math.Sin(-rs) * h / 2.0 + 0.5);
        int xe = (int)(xr + Math.Cos(re) * w / 2.0 + 0.5), ye = (int)(yr + Math.Sin(-re) * h / 2.0 + 0.5);
        if (xs == xe && ys == ye && !full)
        {
            // equal points would make GDI draw the whole ellipse
            if (rs > Math.PI / 4)
            {
                if (rs < 3 * Math.PI / 4)
                    xe--;
                else if (rs < 5 * Math.PI / 4)
                    ye++;
                else if (rs < 7 * Math.PI / 4)
                    xe++;
                else
                    ye--;
            }
            else
            {
                ye--;
            }
        }
        if (fill)
        {
            Native.Pie(dc, x, y, x + w + 1, y + h + 1, xs, ys, xe, ye);
            return;
        }
        Native.SetBkMode(dc, Native.Transparent);
        Native.Arc(dc, x, y, x + w + 1, y + h + 1, xs, ys, xe, ye);
    }

    static void Paint(IntPtr dc, Key key)
    {
        int w = key.X2 - key.X1, h = key.Y2 - key.Y1;
        switch (key.Kind)
        {
            case Kind.FillOval:
            {
                // the fill GC has line width 0: a cosmetic pen traces the edge
                var pen = Native.SelectObject(dc, Pen(0, 0));
                var brush = Native.SelectObject(dc, Native.CreateSolidBrush(White));
                ArcCall(dc, key.X1, key.Y1, w, h, key.A, key.B, true);
                Native.DeleteObject(Native.SelectObject(dc, brush));
                Native.DeleteObject(Native.SelectObject(dc, pen));
                break;
            }
            case Kind.OutlineOval:
            case Kind.Arc:
            {
                var cap = key.Kind == Kind.OutlineOval ? Native.PsEndcapSquare : Native.PsEndcapFlat;
                var pen = Native.SelectObject(dc, Pen(key.Width, cap | Native.PsJoinMiter));
                ArcCall(dc, key.X1, key.Y1, w, h, key.A, key.B, false);
                Native.DeleteObject(Native.SelectObject(dc, pen));
                break;
            }
            case Kind.Line:
            {
                // XDrawLines strokes a path; it is closed only when the ends meet
                var pen = Native.SelectObject(dc, Pen(key.Width, Native.PsEndcapFlat | Native.PsJoinRound));
                Native.SetBkMode(dc, Native.Transparent);
                Native.BeginPath(dc);
                Native.Polyline(dc, [new() { X = key.X1, Y = key.Y1 }, new() { X = key.X2, Y = key.Y2 }], 2);
                Native.EndPath(dc);
                Native.StrokePath(dc);
                Native.DeleteObject(Native.SelectObject(dc, pen));
                break;
            }
        }
    }

    /// <summary>White on black in a bitmap around the shape, then the touched pixels in the color.</summary>
    static Stamp? Render(Key key)
    {
        var pad = key.Width + 2;
        int left = Math.Min(key.X1, key.X2) - pad, top = Math.Min(key.Y1, key.Y2) - pad;
        int width = Math.Abs(key.X2 - key.X1) + 1 + 2 * pad, height = Math.Abs(key.Y2 - key.Y1) + 1 + 2 * pad;
        var pixels = Capture(left, top, width, height, dc => Paint(dc, key));
        return pixels is null ? null : Colorize(pixels, width, height, left, top, key.Color);
    }

    /// <summary>Tk's GetScreenFont: the face at a pixel height, everything else left to Windows.
    /// Kept for the life of the process, as Tk keeps its fonts.</summary>
    static IntPtr Font(string face, int pixels, bool bold)
    {
        var key = (face, pixels, bold);
        if (!Fonts.TryGetValue(key, out var font))
            Fonts[key] = font = Native.CreateFont(-pixels, 0, 0, 0, bold ? Native.FwBold : Native.FwNormal, 0, 0, 0,
                                                  Native.DefaultCharset, Native.OutTtPrecis, 0, 0, 0, face);
        return font;
    }

    /// <summary>A run's advance in whole pixels: how Tk measures text.</summary>
    public static int Measure(string text, string face, int pixels, bool bold = false)
    {
        var key = (text, face, pixels, bold);
        if (Widths.TryGetValue(key, out var width))
            return width;
        if (Widths.Count >= CacheLimit)
            Widths.Clear();
        var dc = Measurer();
        var old = Native.SelectObject(dc, Font(face, pixels, bold));
        Native.GetTextExtentPoint32(dc, text, text.Length, out var size);
        Native.SelectObject(dc, old);
        return Widths[key] = size.Cx;
    }

    /// <summary>GDI's whole-pixel ascent and descent: Tk's line box for the face.</summary>
    public static (int Ascent, int Descent) Metrics(string face, int pixels, bool bold = false)
    {
        var dc = Measurer();
        var old = Native.SelectObject(dc, Font(face, pixels, bold));
        Native.GetTextMetrics(dc, out var metrics);
        Native.SelectObject(dc, old);
        return (metrics.Ascent, metrics.Descent);
    }

    static IntPtr Measurer() => measurer == IntPtr.Zero ? measurer = Native.CreateCompatibleDC(IntPtr.Zero) : measurer;

    /// <summary>A text run as GDI draws it in the face, from its origin on the baseline, and its
    /// advance. Tk hands a character its font lacks to another face, and at small sizes that face
    /// may answer with an embedded bitmap, which WPF does not render.</summary>
    public static (Stamp? Ink, int Advance) Text(string text, string face, int pixels, Color color)
    {
        var key = (text, face, pixels, color);
        if (Texts.TryGetValue(key, out var cached))
            return cached;
        var font = Font(face, pixels, false);
        var advance = Measure(text, face, pixels);
        int left = -pixels, top = -2 * pixels, width = advance + 2 * pixels, height = 3 * pixels;
        var bits = Capture(left, top, width, height, dc =>
        {
            var previous = Native.SelectObject(dc, font);
            Native.SetTextColor(dc, White);
            Native.SetBkMode(dc, Native.Transparent);
            Native.SetTextAlign(dc, Native.TaBaseline);
            Native.TextOut(dc, 0, 0, text, text.Length);
            Native.SelectObject(dc, previous);
        });
        return Texts[key] = (bits is null ? null : Colorize(bits, width, height, left, top, color), advance);
    }

    /// <summary>What the painter leaves in a black 32 bit bitmap over the given device rectangle.</summary>
    static byte[]? Capture(int left, int top, int width, int height, Action<IntPtr> paint)
    {
        var pixels = new byte[width * height * 4];
        var dc = Native.CreateCompatibleDC(IntPtr.Zero);
        try
        {
            var info = new Native.BitmapInfoHeader
            {
                Size = Marshal.SizeOf<Native.BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32,
            };
            var bitmap = Native.CreateDIBSection(dc, info, Native.DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero)
                return null;
            var old = Native.SelectObject(dc, bitmap);
            Native.SetViewportOrgEx(dc, -left, -top, IntPtr.Zero);
            paint(dc);
            Native.GdiFlush();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            Native.SelectObject(dc, old);
            Native.DeleteObject(bitmap);
        }
        finally
        {
            Native.DeleteDC(dc);
        }
        return pixels;
    }

    /// <summary>The ink's bounding box as an image: the color where GDI drew, as opaque as the ink
    /// is bright, clear elsewhere. Shapes are pure white, so only text can come out partly clear.</summary>
    static Stamp? Colorize(byte[] pixels, int width, int height, int left, int top, Color color)
    {
        int x0 = width, y0 = height, x1 = -1, y1 = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) == 0)
                    continue;
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
            }
        if (x1 < 0)
            return null;
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var image = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = ((y + y0) * width + x + x0) * 4;
                if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) == 0)
                    continue;
                var o = (y * w + x) * 4;
                image[o] = color.B;
                image[o + 1] = color.G;
                image[o + 2] = color.R;
                image[o + 3] = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
            }
        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, image, w * 4);
        source.Freeze();
        return new Stamp(source, left + x0, top + y0);
    }
}
