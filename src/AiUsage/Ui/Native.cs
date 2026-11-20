using System;
using System.Runtime.InteropServices;

namespace AlData.AiUsage.Ui;

/// <summary>The few Win32 calls WPF does not wrap. Coordinates are physical pixels.</summary>
internal static partial class Native
{
    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndNoTopmost = new(-2);
    public const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x00000080, WsExAppWindow = 0x00040000, WsExNoActivate = 0x08000000;
    public const long WsExLayered = 0x00080000;
    public const uint LwaAlpha = 2;
    public const int SmCxScreen = 0, SmCyScreen = 1, SmCxSmIcon = 49;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>The whole window at one opacity, as Tk's -alpha does.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    // --- GDI, for the canvas shapes in TkDraw ------------------------------
    public const int PsSolid = 0, BsSolid = 0, Transparent = 1, DibRgbColors = 0;
    public const uint PsGeometric = 0x00010000, PsEndcapSquare = 0x100, PsEndcapFlat = 0x200;
    public const uint PsJoinRound = 0, PsJoinMiter = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LogBrush
    {
        public uint Style, Color;
        public IntPtr Hatch;
    }

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateDIBSection(IntPtr dc, in BitmapInfoHeader info, uint usage, out IntPtr bits,
                                                  IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetViewportOrgEx(IntPtr dc, int x, int y, IntPtr previous);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreatePen(int style, int width, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr ExtCreatePen(uint style, uint width, in LogBrush brush, uint count, IntPtr styles);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(IntPtr dc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Pie(IntPtr dc, int left, int top, int right, int bottom, int x1, int y1, int x2, int y2);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Arc(IntPtr dc, int left, int top, int right, int bottom, int x1, int y1, int x2, int y2);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BeginPath(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPath(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StrokePath(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Polyline(IntPtr dc, Point[] points, int count);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiFlush();

    // --- GDI text, for the glyphs Tk takes from a fallback font -------------
    public const int FwNormal = 400, FwBold = 700;
    public const uint DefaultCharset = 1, OutTtPrecis = 4, TaBaseline = 24;

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight,
                   Overhang, DigitizedAspectX, DigitizedAspectY;
        public ushort FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextMetrics(IntPtr dc, out TextMetric metrics);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight,
                                            uint italic, uint underline, uint strikeOut, uint charSet,
                                            uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily,
                                            string face);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32(IntPtr dc, string text, int count, out Size size);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TextOut(IntPtr dc, int x, int y, string text, int count);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(IntPtr dc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextAlign(IntPtr dc, uint align);

    // --- monitors and windows, for the menu --------------------------------
    public const uint MonitorDefaultToNearest = 2;
    public const int MdtEffectiveDpi = 0;
    public const int DwmwaWindowCornerPreference = 33, DwmwaBorderColor = 34, DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, in int value, int size);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    // --- packaging, for autostart ------------------------------------------
    public const int AppModelErrorNoPackage = 15700;

    [LibraryImport("kernel32.dll")]
    public static partial int GetCurrentPackageFamilyName(ref uint length, IntPtr name);

    // --- restart after an update -------------------------------------------
    /// <summary>RegisterApplicationRestart flags: no restart after a crash, a hang or a reboot,
    /// which leaves only the one after an update.</summary>
    public const uint RestartNoCrash = 1, RestartNoHang = 2, RestartNoReboot = 8;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterApplicationRestart(string? commandLine, uint flags);
}
