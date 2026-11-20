using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlData.AiUsage.Ui.Layouts;

/// <summary>An RGBA bitmap that draws the way Pillow's ImageDraw does, so the icon skins come out
/// as the Tk prototype drew them, pixel for pixel: coordinates truncated to whole pixels, the scanline
/// polygon, the ellipse and arc iterators, Lanczos resampling. Ported from Pillow 12 (Draw.c,
/// Resample.c, Convert.c); the comments name the C functions. Ink is opaque and replaces what is
/// under it, as ImageDraw does on an RGBA image.</summary>
public sealed class Raster(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    /// <summary>R, G, B, A per pixel, row by row; transparent black to start with.</summary>
    public byte[] Pixels { get; } = new byte[width * height * 4];

    // --- primitives: point32, hline32, line32 ----------------------------
    void Set(int x, int y, Color ink)
    {
        var i = (y * Width + x) * 4;
        Pixels[i] = ink.R;
        Pixels[i + 1] = ink.G;
        Pixels[i + 2] = ink.B;
        Pixels[i + 3] = ink.A;
    }

    void Point(int x, int y, Color ink)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
            Set(x, y, ink);
    }

    /// <summary>Both ends included; nothing when x1 &lt; x0.</summary>
    void HLine(int x0, int y, int x1, Color ink)
    {
        if (y < 0 || y >= Height)
            return;
        if (x0 < 0)
            x0 = 0;
        else if (x0 >= Width)
            return;
        if (x1 < 0)
            return;
        if (x1 >= Width)
            x1 = Width - 1;
        for (; x0 <= x1; x0++)
            Set(x0, y, ink);
    }

    /// <summary>Bresenham without the last point.</summary>
    void Bresenham(int x0, int y0, int x1, int y1, Color ink)
    {
        int dx = x1 - x0, dy = y1 - y0, xs = 1, ys = 1;
        if (dx < 0)
            (dx, xs) = (-dx, -1);
        if (dy < 0)
            (dy, ys) = (-dy, -1);
        if (dx == 0)
        {
            for (var i = 0; i < dy; i++, y0 += ys)
                Point(x0, y0, ink);
        }
        else if (dy == 0)
        {
            for (var i = 0; i < dx; i++, x0 += xs)
                Point(x0, y0, ink);
        }
        else if (dx > dy)
        {
            var n = dx;
            dy += dy;
            var e = dy - dx;
            dx += dx;
            for (var i = 0; i < n; i++, x0 += xs)
            {
                Point(x0, y0, ink);
                if (e >= 0)
                {
                    y0 += ys;
                    e -= dx;
                }
                e += dy;
            }
        }
        else
        {
            var n = dy;
            dx += dx;
            var e = dx - dy;
            dy += dy;
            for (var i = 0; i < n; i++, y0 += ys)
            {
                Point(x0, y0, ink);
                if (e >= 0)
                {
                    x0 += xs;
                    e -= dy;
                }
                e += dx;
            }
        }
    }

    // ROUND_UP and ROUND_DOWN. For a float, f + 0.5F stays a float in C; fabs() makes a double.
    static int RoundUp(float f) => (int)(f >= 0.0 ? Math.Floor(f + 0.5f) : -Math.Floor(Math.Abs((double)f) + 0.5));
    static int RoundDown(float f) => (int)(f >= 0.0 ? Math.Ceiling(f - 0.5f) : -Math.Ceiling(Math.Abs((double)f) - 0.5));
    static int RoundUp(double f) => (int)(f >= 0.0 ? Math.Floor(f + 0.5) : -Math.Floor(Math.Abs(f) + 0.5));
    static int RoundDown(double f) => (int)(f >= 0.0 ? Math.Ceiling(f - 0.5) : -Math.Ceiling(Math.Abs(f) - 0.5));

    // --- polygons: add_edge, polygon_generic -------------------------------
    sealed class Edge
    {
        public readonly int X0, Y0, YMin, YMax;
        public int XMin, XMax;
        public readonly float Dx;

        public Edge(int x0, int y0, int x1, int y1)
        {
            (XMin, XMax) = x0 <= x1 ? (x0, x1) : (x1, x0);
            (YMin, YMax) = y0 <= y1 ? (y0, y1) : (y1, y0);
            Dx = y0 == y1 ? 0 : (float)(x1 - x0) / (y1 - y0);
            X0 = x0;
            Y0 = y0;
        }

        public float At(int y) => (y - Y0) * Dx + X0;
    }

    static float RoundF(float f) => MathF.Round(f, MidpointRounding.AwayFromZero);

    void Fill(List<Edge> edges, Color ink)
    {
        if (edges.Count == 0)
            return;
        int ymin = Height - 1, ymax = 0;
        var table = new List<Edge>(edges.Count);
        foreach (var edge in edges)
        {
            ymin = Math.Min(ymin, edge.YMin);
            ymax = Math.Max(ymax, edge.YMax);
            if (edge.YMin == edge.YMax)
                HLine(edge.XMin, edge.YMin, edge.XMax, ink);
            else
                table.Add(edge);
        }
        ymin = Math.Max(ymin, 0);
        ymax = Math.Min(ymax, Height);
        var xx = new float[table.Count * 2];
        for (; ymin <= ymax; ymin++)
        {
            var j = 0;
            for (var i = 0; i < table.Count; i++)
            {
                var current = table[i];
                if (ymin < current.YMin || ymin > current.YMax)
                    continue;
                xx[j++] = current.At(ymin);
                if (ymin == current.YMax && ymin < ymax)
                {
                    // needed to draw consistent polygons
                    xx[j] = xx[j - 1];
                    j++;
                }
                else if ((ymin == current.YMin || ymin == current.YMax) && current.Dx != 0)
                {
                    // connect discontiguous corners
                    for (var k = 0; k < i; k++)
                    {
                        var other = table[k];
                        if ((ymin != other.YMin && ymin != other.YMax) || other.Dx == 0)
                            continue;
                        if (RoundF(xx[j - 1]) != RoundF(other.At(ymin)))
                            continue;
                        var offset = ymin == current.YMax ? -1 : 1;
                        if (ymin + offset < other.YMin || ymin + offset > other.YMax)
                            continue;
                        var adjacent = current.At(ymin + offset);
                        var adjacentOther = other.At(ymin + offset);
                        if (xx[j - 1] > adjacent + 1 && xx[j - 1] > adjacentOther + 1)
                            xx[j - 1] = RoundF(MathF.Max(adjacent, adjacentOther)) + 1;
                        else if (xx[j - 1] < adjacent - 1 && xx[j - 1] < adjacentOther - 1)
                            xx[j - 1] = RoundF(MathF.Min(adjacent, adjacentOther)) - 1;
                        break;
                    }
                }
            }
            Array.Sort(xx, 0, j);
            for (var i = 1; i < j; i += 2)
                HLine(RoundUp(xx[i - 1]), ymin, RoundDown(xx[i]), ink);
        }
    }

    // --- shapes, as ImageDraw calls them with fill= ----------------------
    public void Rectangle(double left, double top, double right, double bottom, Color ink)
    {
        int x0 = (int)left, y0 = (int)top, x1 = (int)right, y1 = (int)bottom;
        if (y0 > y1)
            (y0, y1) = (y1, y0);
        if (y0 < 0)
            y0 = 0;
        else if (y0 >= Height)
            return;
        if (y1 < 0)
            return;
        if (y1 > Height)
            y1 = Height;
        for (var y = y0; y <= y1; y++)
            HLine(x0, y, x1, ink);
    }

    /// <summary>ImagingDrawPolygon: a horizontal edge right after another one going the same way
    /// is merged into it.</summary>
    public void Polygon(Color ink, params (double X, double Y)[] points)
    {
        var count = points.Length;
        if (count == 0)
            return;
        var xy = new int[count * 2];
        for (var i = 0; i < count; i++)
        {
            xy[i * 2] = (int)points[i].X;
            xy[i * 2 + 1] = (int)points[i].Y;
        }
        var edges = new List<Edge>(count);
        int n;
        for (n = 0; n < count - 1; n++)
        {
            int x0 = xy[n * 2], y0 = xy[n * 2 + 1], x1 = xy[n * 2 + 2], y1 = xy[n * 2 + 3];
            if (y0 == y1 && n != 0 && y0 == xy[n * 2 - 1])
            {
                var last = edges[^1];
                if (x1 > x0 && x0 > xy[n * 2 - 2])
                {
                    last.XMax = x1;
                    continue;
                }
                if (x1 < x0 && x0 < xy[n * 2 - 2])
                {
                    last.XMin = x1;
                    continue;
                }
            }
            edges.Add(new Edge(x0, y0, x1, y1));
        }
        if (xy[n * 2] != xy[0] || xy[n * 2 + 1] != xy[1])
            edges.Add(new Edge(xy[n * 2], xy[n * 2 + 1], xy[0], xy[1]));
        Fill(edges, ink);
    }

    /// <summary>A line of the given width: Bresenham with the end point for 1, a filled
    /// quadrilateral otherwise (ImagingDrawWideLine).</summary>
    public void Line(double ax, double ay, double bx, double by, Color ink, int width)
    {
        int x0 = (int)ax, y0 = (int)ay, x1 = (int)bx, y1 = (int)by;
        if (width <= 1)
        {
            Bresenham(x0, y0, x1, y1, ink);
            Point(x1, y1, ink);
            return;
        }
        int dx = x1 - x0, dy = y1 - y0;
        if (dx == 0 && dy == 0)
        {
            Point(x0, y0, ink);
            return;
        }
        var big = double.Hypot(dx, dy);
        var small = (width - 1) / 2.0;
        var ratioMax = RoundUp(small) / big;
        var ratioMin = RoundDown(small) / big;
        int dxmin = RoundDown(ratioMin * dy), dxmax = RoundDown(ratioMax * dy);
        int dymin = RoundDown(ratioMin * dx), dymax = RoundDown(ratioMax * dx);
        (int X, int Y)[] v =
        [
            (x0 - dxmin, y0 + dymax),
            (x1 - dxmin, y1 + dymax),
            (x1 + dxmax, y1 - dymin),
            (x0 + dxmax, y0 - dymin),
        ];
        var edges = new List<Edge>(4);
        for (var i = 0; i < 4; i++)
            edges.Add(new Edge(v[i].X, v[i].Y, v[(i + 1) % 4].X, v[(i + 1) % 4].Y));
        Fill(edges, ink);
    }

    // --- ellipses: quarter_*, ellipse_* ----------------------------------
    /// <summary>The top right quarter of an ellipse on a grid of step 2 (quarter_state).</summary>
    sealed class Quarter
    {
        readonly int ex, ey;
        readonly long a2, b2, a2b2;
        int cx, cy;
        bool finished;

        public Quarter(int a, int b)
        {
            if (a < 0 || b < 0)
            {
                finished = true;
                return;
            }
            cx = a;
            cy = b % 2;
            ex = a % 2;
            ey = b;
            a2 = a * a;
            b2 = b * b;
            a2b2 = a2 * b2;
        }

        long Delta(long x, long y) => Math.Abs(a2 * y * y + b2 * x * x - a2b2);

        /// <summary>False when done; then x and y are left as they were.</summary>
        public bool Next(ref int x, ref int y)
        {
            if (finished)
                return false;
            x = cx;
            y = cy;
            if (cx == ex && cy == ey)
            {
                finished = true;
                return true;
            }
            int nx = cx, ny = cy + 2;
            var ndelta = Delta(nx, ny);
            if (nx > 1)
            {
                var newdelta = Delta(cx - 2, cy + 2);
                if (ndelta > newdelta)
                {
                    nx = cx - 2;
                    ny = cy + 2;
                    ndelta = newdelta;
                }
                newdelta = Delta(cx - 2, cy);
                if (ndelta > newdelta)
                {
                    nx = cx - 2;
                    ny = cy;
                }
            }
            cx = nx;
            cy = ny;
            return true;
        }
    }

    /// <summary>Horizontal segments of a ring between two ellipses, w thick (ellipse_state).</summary>
    sealed class Ring
    {
        readonly Quarter outer, inner;
        readonly int leftmost;
        readonly int[] bufL = new int[4], bufY = new int[4], bufR = new int[4];
        int py, pl, pr, count;
        bool finished;

        public Ring(int a, int b, int w)
        {
            leftmost = a % 2;
            outer = new Quarter(a, b);
            inner = new Quarter(a - 2 * (w - 1), b - 2 * (w - 1));
            finished = w < 1 || !outer.Next(ref pr, ref py);
            pl = leftmost;
        }

        void Push(int l, int y, int r)
        {
            bufL[count] = l;
            bufY[count] = y;
            bufR[count] = r;
            count++;
        }

        public bool Next(out int x0, out int y, out int x1)
        {
            if (count == 0)
            {
                if (finished)
                {
                    x0 = y = x1 = 0;
                    return false;
                }
                int row = py, l = pl, r = pr, cx = 0, cy = 0;
                bool more;
                while ((more = outer.Next(ref cx, ref cy)) && cy <= row)
                {
                }
                if (more)
                    (pr, py) = (cx, cy);
                else
                    finished = true;
                while ((more = inner.Next(ref cx, ref cy)) && cy <= row)
                    l = cx;
                pl = more ? cx : leftmost;
                if ((l > 0 || l < r) && row > 0)
                    Push(l == 0 ? 2 : l, row, r);
                if (row > 0)
                    Push(-r, row, -l);
                if (l > 0 || l < r)
                    Push(l == 0 ? 2 : l, -row, r);
                Push(-r, -row, -l);
            }
            count--;
            x0 = bufL[count];
            y = bufY[count];
            x1 = bufR[count];
            return true;
        }
    }

    public void Ellipse(double left, double top, double right, double bottom, Color ink)
    {
        int x0 = (int)left, y0 = (int)top, a = (int)right - x0, b = (int)bottom - y0;
        if (a < 0 || b < 0)
            return;
        var ring = new Ring(a, b, a + b);
        while (ring.Next(out var l, out var y, out var r))
            HLine(x0 + (l + a) / 2, y0 + (y + b) / 2, x0 + (r + a) / 2, ink);
    }

    // --- arcs: normalize_angles, arc_init, clip_tree_do_clip --------------
    enum Clip { And, Or, Half }

    /// <summary>A half-plane A·x + B·y + C ≥ 0, or the intersection or union of two subtrees.</summary>
    sealed class ClipNode(Clip kind)
    {
        public readonly Clip Kind = kind;
        public double A, B, C;
        public ClipNode? L, R;
    }

    /// <summary>After this 0 ≤ al &lt; 360 and al ≤ ar ≤ al + 360, in float as C has it.</summary>
    static void NormalizeAngles(ref float al, ref float ar)
    {
        if (ar - al >= 360)
        {
            al = 0;
            ar = 360;
            return;
        }
        al = (float)((al < 0 ? 360 - (double)-al % 360 : al) % 360);
        ar = (float)(al + (ar < al ? 360 - (double)(al - ar) % 360 : (double)(ar - al)) % 360);
    }

    static void Transpose(ClipNode? node)
    {
        if (node is null)
            return;
        if (node.Kind == Clip.Half)
            (node.A, node.B) = (node.B, node.A);
        Transpose(node.L);
        Transpose(node.R);
    }

    /// <summary>The clipping tree of an arc with caps square to the curve; null for a whole ring.</summary>
    static ClipNode? ArcClip(int a, int b, float al, float ar)
    {
        if (a < b)
        {
            // transpose the coordinate system
            var root = ArcClip(b, a, 90 - ar, 90 - al);
            Transpose(root);
            return root;
        }
        NormalizeAngles(ref al, ref ar);
        if (ar == al + 360)
            return null;
        var lc = new ClipNode(Clip.Half)
        {
            A = -a * Math.Sin(al * Math.PI / 180.0),
            B = b * Math.Cos(al * Math.PI / 180.0),
            C = (a * a - b * b) * Math.Sin(al * Math.PI / 90.0) / 2.0,
        };
        var rc = new ClipNode(Clip.Half)
        {
            A = a * Math.Sin(ar * Math.PI / 180.0),
            B = -b * Math.Cos(ar * Math.PI / 180.0),
            C = (b * b - a * a) * Math.Sin(ar * Math.PI / 90.0) / 2.0,
        };
        var join = ar - al < 180 ? Clip.And : Clip.Or;
        if ((double)al % 180 == 0 || (double)ar % 180 == 0)
            return new ClipNode(join) { L = lc, R = rc };
        if (((int)(al / 180) + (int)(ar / 180)) % 2 == 1)
        {
            return new ClipNode(Clip.Or)
            {
                L = new ClipNode(Clip.And) { L = new ClipNode(Clip.Half) { B = (int)(al / 180) % 2 == 0 ? 1 : -1 }, R = lc },
                R = new ClipNode(Clip.And) { L = new ClipNode(Clip.Half) { B = (int)(ar / 180) % 2 == 0 ? 1 : -1 }, R = rc },
            };
        }
        return new ClipNode(join)
        {
            L = new ClipNode(join) { L = lc, R = rc },
            R = new ClipNode(Clip.Half) { B = ar < 180 || ar > 540 ? 1 : -1 },
        };
    }

    /// <summary>Ends of the parts of the segment the tree lets through, in order: +1 opens, -1 closes.</summary>
    static List<(int X, int Type)> DoClip(ClipNode? root, int x0, int y, int x1)
    {
        if (root is null)
            return [(x0, 1), (x1, -1)];
        if (root.Kind == Clip.Half)
        {
            const double eps = 1e-9;
            double a = root.A, b = root.B, c = root.C;
            if (Math.Abs(a) < eps)
            {
                if (b * y + c < -eps)
                    (x0, x1) = (1, 0);
            }
            else
            {
                var ix = -(b * y + c) / a;
                if (a * x0 + b * y + c < eps)
                    x0 = (int)Math.Round(Math.Max(x0, ix), MidpointRounding.AwayFromZero);
                if (a * x1 + b * y + c < eps)
                    x1 = (int)Math.Round(Math.Min(x1, ix), MidpointRounding.AwayFromZero);
            }
            return x0 <= x1 ? [(x0, 1), (x1, -1)] : [];
        }
        var l1 = DoClip(root.L, x0, y, x1);
        var l2 = DoClip(root.R, x0, y, x1);
        var result = new List<(int X, int Type)>();
        int i1 = 0, i2 = 0, k1 = 0, k2 = 0;
        while (i1 < l1.Count || i2 < l2.Count)
        {
            (int X, int Type) t;
            if (i2 == l2.Count || (i1 < l1.Count && (l1[i1].X < l2[i2].X || (l1[i1].X == l2[i2].X && l1[i1].Type > l2[i2].Type))))
            {
                t = l1[i1++];
                k1 += t.Type;
            }
            else
            {
                t = l2[i2++];
                k2 += t.Type;
            }
            var open = result.Count > 0 && result[^1].Type == 1;
            var keep = root.Kind == Clip.Or
                ? (t.Type == 1 && !open) || (t.Type == -1 && k1 == 0 && k2 == 0)
                : (t.Type == 1 && !open && k1 > 0 && k2 > 0) || (t.Type == -1 && open && (k1 == 0 || k2 == 0));
            if (keep)
                result.Add(t);
        }
        return result;
    }

    /// <summary>ImagingDrawArc: degrees clockwise from three o'clock, width pixels thick.</summary>
    public void Arc(double left, double top, double right, double bottom, float start, float end, Color ink, int width)
    {
        int x0 = (int)left, y0 = (int)top, a = (int)right - x0, b = (int)bottom - y0;
        NormalizeAngles(ref start, ref end);
        if (start == end || a < 0 || b < 0)
            return;
        var root = start + 360 == end ? null : ArcClip(a, b, start, end);
        var ring = new Ring(a, b, width);
        while (ring.Next(out var l, out var y, out var r))
        {
            var parts = DoClip(root, l, y, r);
            for (var i = 0; i + 1 < parts.Count; i += 2)
                HLine(x0 + (parts[i].X + a) / 2, y0 + (y + b) / 2, x0 + (parts[i + 1].X + a) / 2, ink);
        }
    }

    // --- text ------------------------------------------------------------
    /// <summary>One character centered on (cx, cy), as ImageDraw.text with anchor "mm": the
    /// advance is centered on whole pixels, the baseline sits on a whole row. Coverage comes from
    /// WPF, not FreeType, so the edges can differ by a shade.</summary>
    public void Glyph(GlyphTypeface face, char ch, double size, double cx, double cy, Color ink)
    {
        if (!face.CharacterToGlyphMap.TryGetValue(ch, out var index))
            return;
        var advance = (int)Math.Round(face.AdvanceWidths[index] * size, MidpointRounding.AwayFromZero);
        var outline = face.GetGlyphOutline(index, size, size);
        var bounds = outline.Bounds;
        if (bounds.IsEmpty)
            return;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform((int)cx - (advance + 1) / 2,
                                                    Math.Round(cy - (bounds.Top + bounds.Bottom) / 2)));
            dc.DrawGeometry(Brushes.White, null, outline);
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var coverage = new byte[Width * Height * 4];
        bitmap.CopyPixels(coverage, Width * 4, 0);
        // fill_mask_L on a transparent pixel: the color is the ink, the alpha is the coverage.
        for (var i = 0; i < coverage.Length; i += 4)
        {
            if (coverage[i + 3] == 0)
                continue;
            Pixels[i] = ink.R;
            Pixels[i + 1] = ink.G;
            Pixels[i + 2] = ink.B;
            Pixels[i + 3] = coverage[i + 3];
        }
    }

    // --- whole-image operations --------------------------------------------
    /// <summary>Columns up to edge from full, the rest from empty: Image.composite with a mask
    /// rectangle from 0 to edge.</summary>
    public static Raster Stitch(Raster full, Raster empty, double edge)
    {
        var image = new Raster(full.Width, full.Height);
        var split = (Math.Min((int)edge, full.Width - 1) + 1) * 4;
        var stride = full.Width * 4;
        for (var row = 0; row < image.Pixels.Length; row += stride)
        {
            Array.Copy(full.Pixels, row, image.Pixels, row, split);
            Array.Copy(empty.Pixels, row + split, image.Pixels, row + split, stride - split);
        }
        return image;
    }

    /// <summary>First row with ink and the one past the last, as Image.getbbox has them.</summary>
    public (int Top, int Bottom)? InkRows()
    {
        int top = -1, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (Pixels[(y * Width + x) * 4 + 3] == 0)
                    continue;
                if (top < 0)
                    top = y;
                bottom = y + 1;
                break;
            }
        }
        return top < 0 ? null : (top, bottom);
    }

    /// <summary>Moved down by dy rows (up if negative); what moves out is lost.</summary>
    public Raster Shifted(int dy)
    {
        var image = new Raster(Width, Height);
        var stride = Width * 4;
        for (var y = Math.Max(0, dy); y < Math.Min(Height, Height + dy); y++)
            Array.Copy(Pixels, (y - dy) * stride, image.Pixels, y * stride, stride);
        return image;
    }

    // --- Lanczos: precompute_coeffs, ImagingResampleHorizontal/Vertical_8bpc ---
    const int PrecisionBits = 32 - 8 - 2;

    static double Sinc(double x)
    {
        if (x == 0.0)
            return 1.0;
        x *= Math.PI;
        return Math.Sin(x) / x;
    }

    static double Lanczos(double x) => -3.0 <= x && x < 3.0 ? Sinc(x) * Sinc(x / 3) : 0.0;

    /// <summary>Per output pixel: the first input pixel, how many, and fixed-point weights.</summary>
    static (int[] Bounds, int[] Weights, int KSize) Coefficients(int inSize, int outSize)
    {
        var scale = (double)inSize / outSize;
        var filterscale = Math.Max(scale, 1.0);
        var support = 3.0 * filterscale;
        var ksize = (int)Math.Ceiling(support) * 2 + 1;
        var bounds = new int[outSize * 2];
        var weights = new int[outSize * ksize];
        var k = new double[ksize];
        for (var xx = 0; xx < outSize; xx++)
        {
            var center = (xx + 0.5) * scale;
            var ss = 1.0 / filterscale;
            var xmin = Math.Max(0, (int)(center - support + 0.5));
            var xmax = Math.Min(inSize, (int)(center + support + 0.5)) - xmin;
            var ww = 0.0;
            for (var x = 0; x < xmax; x++)
            {
                k[x] = Lanczos((x + xmin - center + 0.5) * ss);
                ww += k[x];
            }
            for (var x = 0; x < xmax; x++)
            {
                if (ww != 0.0)
                    k[x] /= ww;
                weights[xx * ksize + x] = (int)(k[x] < 0 ? -0.5 + k[x] * (1 << PrecisionBits) : 0.5 + k[x] * (1 << PrecisionBits));
            }
            bounds[xx * 2] = xmin;
            bounds[xx * 2 + 1] = xmax;
        }
        return (bounds, weights, ksize);
    }

    static byte Clip8(int value) => (byte)Math.Clamp(value >> PrecisionBits, 0, 255);

    /// <summary>One pass over premultiplied pixels: along rows when horizontal, else along columns.</summary>
    static byte[] Pass(byte[] input, int inW, int outW, int outH, bool horizontal)
    {
        var (bounds, weights, ksize) = horizontal ? Coefficients(inW, outW) : Coefficients(input.Length / 4 / inW, outH);
        var output = new byte[outW * outH * 4];
        for (var y = 0; y < outH; y++)
        {
            for (var x = 0; x < outW; x++)
            {
                var o = horizontal ? x : y;
                int first = bounds[o * 2], taps = bounds[o * 2 + 1], kOff = o * ksize;
                int s0 = 1 << (PrecisionBits - 1), s1 = s0, s2 = s0, s3 = s0;
                for (var t = 0; t < taps; t++)
                {
                    var i = horizontal ? (y * inW + first + t) * 4 : ((first + t) * inW + x) * 4;
                    var k = weights[kOff + t];
                    s0 += input[i] * k;
                    s1 += input[i + 1] * k;
                    s2 += input[i + 2] * k;
                    s3 += input[i + 3] * k;
                }
                var j = (y * outW + x) * 4;
                output[j] = Clip8(s0);
                output[j + 1] = Clip8(s1);
                output[j + 2] = Clip8(s2);
                output[j + 3] = Clip8(s3);
            }
        }
        return output;
    }

    /// <summary>Image.resize with LANCZOS: RGBA goes through premultiplied RGBa and back.</summary>
    public Raster Resized(int width, int height)
    {
        var premultiplied = new byte[Pixels.Length];
        for (var i = 0; i < Pixels.Length; i += 4)
        {
            int alpha = Pixels[i + 3];
            for (var c = 0; c < 3; c++)
            {
                var tmp = Pixels[i + c] * alpha + 128;
                premultiplied[i + c] = (byte)(((tmp >> 8) + tmp) >> 8);
            }
            premultiplied[i + 3] = (byte)alpha;
        }
        var rows = Pass(premultiplied, Width, width, Height, horizontal: true);
        var scaled = Pass(rows, width, width, height, horizontal: false);
        var image = new Raster(width, height);
        for (var i = 0; i < scaled.Length; i += 4)
        {
            int alpha = scaled[i + 3];
            for (var c = 0; c < 3; c++)
                image.Pixels[i + c] = alpha is 0 or 255 ? scaled[i + c] : (byte)Math.Min(255, 255 * scaled[i + c] / alpha);
            image.Pixels[i + 3] = (byte)alpha;
        }
        return image;
    }

    /// <summary>Laid over a solid background the way Tk shows a photo image on its canvas, which
    /// truncates the blend; the result is opaque, at the given DPI.</summary>
    public BitmapSource Over(Color bg, double dpi)
    {
        var bgra = new byte[Pixels.Length];
        for (var i = 0; i < Pixels.Length; i += 4)
        {
            int alpha = Pixels[i + 3];
            bgra[i] = (byte)((bg.B * (255 - alpha) + Pixels[i + 2] * alpha) / 255);
            bgra[i + 1] = (byte)((bg.G * (255 - alpha) + Pixels[i + 1] * alpha) / 255);
            bgra[i + 2] = (byte)((bg.R * (255 - alpha) + Pixels[i] * alpha) / 255);
            bgra[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(Width, Height, dpi, dpi, PixelFormats.Bgra32, null, bgra, Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>With its own alpha, for a window that has no background to lay it over.</summary>
    public BitmapSource Clear(double dpi)
    {
        var bgra = new byte[Pixels.Length];
        for (var i = 0; i < Pixels.Length; i += 4)
        {
            bgra[i] = Pixels[i + 2];
            bgra[i + 1] = Pixels[i + 1];
            bgra[i + 2] = Pixels[i];
            bgra[i + 3] = Pixels[i + 3];
        }
        var bitmap = BitmapSource.Create(Width, Height, dpi, dpi, PixelFormats.Bgra32, null, bgra, Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
