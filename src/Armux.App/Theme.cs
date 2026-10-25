using Armux.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Armux.App;

/// <summary>
/// Dark and light design tokens. All armux-drawn surfaces read colors from here, so a theme switch
/// is one call to <see cref="Use"/> followed by a re-render.
/// </summary>
public static class Theme
{
    public static bool Dark { get; private set; } = true;

    public const string UiFont = "Segoe UI Variable Text";
    public const string MonoFont = "Cascadia Mono";

    static readonly Dictionary<string, (uint Dark, uint Light)> Tokens = new()
    {
        ["TitleBar"] = (0xFF1F1F1F, 0xFFF0F0F0),
        ["Sidebar"] = (0xFF1F1F1F, 0xFFF0F0F0),
        ["SidebarHover"] = (0xFF2A2A2A, 0xFFE4E4E4),
        ["Pane"] = (0xFF262626, 0xFFFFFFFF),
        ["TabBar"] = (0xFF2C2C2C, 0xFFF5F5F5),
        ["TabActive"] = (0xFF262626, 0xFFFFFFFF),
        ["Divider"] = (0xFF161616, 0xFFD9D9D9),
        ["Border"] = (0xFF3A3A3A, 0xFFD4D4D4),
        ["Text"] = (0xFFE8E8E8, 0xFF1F1F1F),
        ["TextDim"] = (0xFFA3A3A3, 0xFF5C5C5C),
        ["TextFaint"] = (0xFF7A7A7A, 0xFF8A8A8A),
        ["Accent"] = (0xFF3B82F6, 0xFF2563EB),
        ["SelectedRow"] = (0xFF3B82F6, 0xFF2563EB),
        ["SelectedText"] = (0xFFFFFFFF, 0xFFFFFFFF),
        ["SelectedDim"] = (0xFFDBE7FE, 0xFFDBE7FE),
        ["Link"] = (0xFF60A5FA, 0xFF2563EB),
        ["Overlay"] = (0x66000000, 0x33000000),
        ["Palette"] = (0xFF2B2B2B, 0xFFFFFFFF),
        ["PaletteSelected"] = (0xFF3B82F6, 0xFF2563EB),
        ["Input"] = (0xFF1F1F1F, 0xFFF3F3F3),
        ["Term.Default"] = (0xFFD4D4D4, 0xFF1F1F1F),
        ["Term.Dim"] = (0xFF8A8A8A, 0xFF6E6E6E),
        ["Term.Green"] = (0xFF4EC97B, 0xFF16A34A),
        ["Term.Blue"] = (0xFF6CA6E8, 0xFF2563EB),
        ["Term.Yellow"] = (0xFFDCC77A, 0xFFA16207),
        ["Term.Red"] = (0xFFF16A6A, 0xFFDC2626),
        ["Term.Magenta"] = (0xFFC792EA, 0xFF9333EA),
        ["Term.Cyan"] = (0xFF56C8E8, 0xFF0E7490),
    };

    static readonly Dictionary<(string, bool), SolidColorBrush> cache = [];

    public static void Use(bool dark) => Dark = dark;

    public static Color Color(string key)
    {
        uint v = Dark ? Tokens[key].Dark : Tokens[key].Light;
        return ColorHelper.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static SolidColorBrush Brush(string key)
    {
        if (!cache.TryGetValue((key, Dark), out var b)) cache[(key, Dark)] = b = new SolidColorBrush(Color(key));
        return b;
    }

    public static SolidColorBrush Term(TermColor c) => Brush("Term." + c);

    public static SolidColorBrush Hex(string hex)
    {
        uint v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new SolidColorBrush(ColorHelper.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v));
    }
}
