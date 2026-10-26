using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Armux.App;

/// <summary>Installed font families, read once through GDI. Monospace families are those GDI reports as fixed pitch.</summary>
public static unsafe partial class FontList
{
    static List<string>? all;
    static HashSet<string>? mono;
    static HashSet<string>? collecting, collectingMono;

    public static IReadOnlyList<string> All { get { Load(); return all!; } }
    public static IReadOnlyList<string> Monospace { get { Load(); return [.. all!.Where(mono!.Contains)]; } }
    public static bool IsInstalled(string family) { Load(); return all!.Contains(family, StringComparer.OrdinalIgnoreCase); }

    static void Load()
    {
        if (all is not null) return;
        collecting = new(StringComparer.OrdinalIgnoreCase);
        collectingMono = new(StringComparer.OrdinalIgnoreCase);
        var lf = new LOGFONTW { lfCharSet = 1 };   // DEFAULT_CHARSET: every family once per charset
        nint dc = GetDC(0);
        try { EnumFontFamiliesExW(dc, &lf, &OnFont, 0, 0); }
        finally { ReleaseDC(0, dc); }
        all = [.. collecting.Order(StringComparer.OrdinalIgnoreCase)];
        mono = collectingMono;
        collecting = collectingMono = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int OnFont(LOGFONTW* lf, void* metrics, uint type, nint param)
    {
        var name = new string(lf->lfFaceName);
        // '@' families are vertical-text variants; 31-character names are GDI-truncated weight variants ("Segoe UI Variable Text Semiligh").
        if (name.Length is > 0 and < 31 && name[0] != '@')
        {
            collecting!.Add(name);
            if ((lf->lfPitchAndFamily & 3) == 1) collectingMono!.Add(name);   // FIXED_PITCH
        }
        return 1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct LOGFONTW
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        public fixed char lfFaceName[32];
    }

    [LibraryImport("gdi32.dll")]
    private static partial int EnumFontFamiliesExW(nint hdc, LOGFONTW* logfont, delegate* unmanaged[Stdcall]<LOGFONTW*, void*, uint, nint, int> proc, nint param, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);
}
