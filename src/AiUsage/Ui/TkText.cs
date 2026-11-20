using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace AlData.AiUsage.Ui;

/// <summary>Segoe UI text placed the way Tk places it, so the widget matches what the Tk prototype drew
/// pixel for pixel. Tk rounds the anchor point, then puts the top of the line box half a GDI
/// line height above it, with GDI's whole-pixel ascent and descent.</summary>
internal static class TkText
{
    static readonly FontFamily Family = new("Segoe UI");
    static readonly Typeface Regular = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface Heavy = new(Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly TextFormatter Formatter = TextFormatter.Create(TextFormattingMode.Display);

    public enum Anchor { W, E, Center }

    /// <summary>Text ready to draw, and its width in whole device pixels.</summary>
    public sealed record Run(string Text, Typeface Typeface, double Size, Brush Brush, double PixelsPerDip, int Pixels)
    {
        /// <summary>The width in DIPs.</summary>
        public double Width => Pixels / PixelsPerDip;
    }

    /// <summary>Segoe UI unless another family is given; the menu draws its icons this way.</summary>
    public static Run Format(string text, double size, bool bold, Brush brush, double pixelsPerDip,
                             FontFamily? family = null)
    {
        var typeface = family is null ? bold ? Heavy : Regular
            : new Typeface(family, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var run = new Run(text, typeface, size, brush, pixelsPerDip, 0);
        return run with { Pixels = Measure(run, bold) };
    }

    /// <summary>GDI measures, as Tk does, unless the face lacks a character: WPF then draws a
    /// fallback of its own choosing, and only WPF knows how wide it is.</summary>
    static int Measure(Run run, bool bold)
    {
        if (run.Typeface.TryGetGlyphTypeface(out var glyphs)
            && run.Text.EnumerateRunes().All(r => glyphs.CharacterToGlyphMap.ContainsKey(r.Value)))
            return TkDraw.Measure(run.Text, run.Typeface.FontFamily.Source, (int)Math.Round(run.Size * run.PixelsPerDip), bold);
        using var line = Line(run);
        return (int)Math.Ceiling(line.WidthIncludingTrailingWhitespace * run.PixelsPerDip);
    }

    /// <summary>Tk's ROUND: half up, in device pixels.</summary>
    public static double Snap(double dip, double pixelsPerDip) => Math.Floor(dip * pixelsPerDip + 0.5) / pixelsPerDip;

    /// <summary>A line of Segoe UI in device pixels: GDI's whole-pixel ascent plus descent.</summary>
    public static int LineHeight(double size, double pixelsPerDip)
    {
        var em = size * pixelsPerDip;
        return (int)(Math.Round(em * Family.Baseline, MidpointRounding.AwayFromZero)
                     + Math.Round(em * (Family.LineSpacing - Family.Baseline), MidpointRounding.AwayFromZero));
    }

    /// <summary>Where Tk puts the baseline of a line anchored on cy, in device pixels.</summary>
    static double Baseline(double cy, double size, double pixelsPerDip, FontFamily family)
    {
        var em = size * pixelsPerDip;
        var ascent = Math.Round(em * family.Baseline, MidpointRounding.AwayFromZero);
        var descent = Math.Round(em * (family.LineSpacing - family.Baseline), MidpointRounding.AwayFromZero);
        return Math.Floor(cy * pixelsPerDip + 0.5) - Math.Floor((ascent + descent) / 2) + ascent;
    }

    public static void Draw(DrawingContext dc, Run run, double x, double cy, double size, Anchor anchor,
                            double pixelsPerDip, FontFamily? family = null)
    {
        var width = run.Pixels;
        var anchorX = Math.Floor(x * pixelsPerDip + 0.5);
        var left = anchor switch
        {
            Anchor.E => anchorX - width,
            Anchor.Center => anchorX - width / 2,
            _ => anchorX,
        };
        DrawOnBaseline(dc, run, left, Baseline(cy, size, pixelsPerDip, family ?? Family));
    }

    /// <summary>A run from a point on its baseline, in device pixels: how Tk draws each line of a label.</summary>
    public static void DrawOnBaseline(DrawingContext dc, Run run, double left, double baseline)
    {
        using var line = Line(run);
        var top = baseline - Math.Round(line.Baseline * run.PixelsPerDip);
        line.Draw(dc, new Point(left / run.PixelsPerDip, top / run.PixelsPerDip), InvertAxes.None);
    }

    /// <summary>A run Segoe UI lacks, drawn by GDI in the face Tk falls back to. Tk keeps the
    /// line box of the base font and measures the run in the fallback.</summary>
    public static void DrawFallback(DrawingContext dc, string text, string face, double x, double cy, double size,
                                    Color color, Anchor anchor, double pixelsPerDip)
    {
        var (ink, advance) = TkDraw.Text(text, face, (int)Math.Round(size * pixelsPerDip), color);
        if (ink is null)
            return;
        var anchorX = Math.Floor(x * pixelsPerDip + 0.5);
        var left = anchor switch
        {
            Anchor.E => anchorX - advance,
            Anchor.Center => anchorX - advance / 2,
            _ => anchorX,
        };
        var baseline = Baseline(cy, size, pixelsPerDip, Family);
        dc.DrawImage(ink.Image, new Rect((left + ink.X) / pixelsPerDip, (baseline + ink.Y) / pixelsPerDip,
                                         ink.Image.PixelWidth / pixelsPerDip, ink.Image.PixelHeight / pixelsPerDip));
    }

    /// <summary>The run laid out as one line, as FormattedText would lay it out but with no
    /// OpenType features: Tk draws through GDI, which neither kerns nor ligates.</summary>
    static TextLine Line(Run run)
    {
        var style = new Style(run.Typeface, run.Size, run.Brush) { PixelsPerDip = run.PixelsPerDip };
        var source = new Source(run.Text, style) { PixelsPerDip = run.PixelsPerDip };
        return Formatter.FormatLine(source, 0, 0, new Paragraph(style), null);
    }

    sealed class Source(string text, Style style) : TextSource
    {
        public override TextRun GetTextRun(int index) =>
            index < text.Length ? new TextCharacters(text, index, text.Length - index, style) : new TextEndOfParagraph(1);

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int index) =>
            new(0, new CultureSpecificCharacterBufferRange(style.CultureInfo, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    sealed class Style(Typeface typeface, double size, Brush brush) : TextRunProperties
    {
        public override Typeface Typeface => typeface;
        public override double FontRenderingEmSize => size;
        public override double FontHintingEmSize => size;
        public override TextDecorationCollection? TextDecorations => null;
        public override Brush ForegroundBrush => brush;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => CultureInfo.CurrentUICulture;
        public override TextEffectCollection? TextEffects => null;
        public override TextRunTypographyProperties TypographyProperties => Plain.Instance;
    }

    sealed class Paragraph(Style style) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => style;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
    }

    /// <summary>Every OpenType feature off.</summary>
    sealed class Plain : TextRunTypographyProperties
    {
        public static readonly Plain Instance = new();

        public override bool StandardLigatures => false;
        public override bool ContextualLigatures => false;
        public override bool DiscretionaryLigatures => false;
        public override bool HistoricalLigatures => false;
        public override int AnnotationAlternates => 0;
        public override bool ContextualAlternates => false;
        public override bool HistoricalForms => false;
        public override bool Kerning => false;
        public override bool CapitalSpacing => false;
        public override bool CaseSensitiveForms => false;
        public override bool StylisticSet1 => false;
        public override bool StylisticSet2 => false;
        public override bool StylisticSet3 => false;
        public override bool StylisticSet4 => false;
        public override bool StylisticSet5 => false;
        public override bool StylisticSet6 => false;
        public override bool StylisticSet7 => false;
        public override bool StylisticSet8 => false;
        public override bool StylisticSet9 => false;
        public override bool StylisticSet10 => false;
        public override bool StylisticSet11 => false;
        public override bool StylisticSet12 => false;
        public override bool StylisticSet13 => false;
        public override bool StylisticSet14 => false;
        public override bool StylisticSet15 => false;
        public override bool StylisticSet16 => false;
        public override bool StylisticSet17 => false;
        public override bool StylisticSet18 => false;
        public override bool StylisticSet19 => false;
        public override bool StylisticSet20 => false;
        public override FontFraction Fraction => FontFraction.Normal;
        public override bool SlashedZero => false;
        public override bool MathematicalGreek => false;
        public override bool EastAsianExpertForms => false;
        public override FontVariants Variants => FontVariants.Normal;
        public override FontCapitals Capitals => FontCapitals.Normal;
        public override FontNumeralStyle NumeralStyle => FontNumeralStyle.Normal;
        public override FontNumeralAlignment NumeralAlignment => FontNumeralAlignment.Normal;
        public override FontEastAsianWidths EastAsianWidths => FontEastAsianWidths.Normal;
        public override FontEastAsianLanguage EastAsianLanguage => FontEastAsianLanguage.Normal;
        public override int StandardSwashes => 0;
        public override int ContextualSwashes => 0;
        public override int StylisticAlternates => 0;
    }
}
