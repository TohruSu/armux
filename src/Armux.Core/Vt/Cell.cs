using System.Globalization;

namespace Armux.Core.Vt;

[Flags]
public enum CellFlags : ushort
{
    None = 0, Bold = 1, Dim = 2, Italic = 4, Underline = 8, Blink = 16, Inverse = 32, Hidden = 64, Strike = 128,
}

/// <summary>
/// A color as stored in a cell: <see cref="Default"/>, a palette index (0-255) or 24-bit RGB.
/// The top byte holds the kind, the low 24 bits the index or RGB value.
/// </summary>
public static class VtColor
{
    public const uint Default = 0;
    const uint IndexedKind = 0x01_000000, RgbKind = 0x02_000000;

    public static uint Indexed(int index) => IndexedKind | (uint)(index & 0xFF);
    public static uint Rgb(int r, int g, int b) => RgbKind | (uint)((r & 0xFF) << 16 | (g & 0xFF) << 8 | (b & 0xFF));
    public static bool IsIndexed(uint c) => (c & 0xFF_000000) == IndexedKind;
    public static bool IsRgb(uint c) => (c & 0xFF_000000) == RgbKind;
    public static int Index(uint c) => (int)(c & 0xFF);
    public static uint RgbValue(uint c) => c & 0xFFFFFF;

    /// <summary>The RGB value of xterm palette entries 16-255 (the 6x6x6 cube and the gray ramp).</summary>
    public static uint Xterm256(int index)
    {
        if (index >= 232) { int v = 8 + (index - 232) * 10; return (uint)(v << 16 | v << 8 | v); }
        index -= 16;
        static int Level(int x) => x == 0 ? 0 : 55 + x * 40;
        return (uint)(Level(index / 36) << 16 | Level(index / 6 % 6) << 8 | Level(index % 6));
    }
}

public readonly record struct Attr(uint Fg, uint Bg, CellFlags Flags)
{
    public static readonly Attr Default = new(VtColor.Default, VtColor.Default, CellFlags.None);
}

/// <summary>
/// One screen cell. <see cref="Rune"/> is 0 for a blank cell and <see cref="WideTail"/> for the right half of a
/// double-width character.
/// </summary>
public record struct Cell(int Rune, Attr Attr)
{
    public const int WideTail = -1;
    public static readonly Cell Blank = new(0, Attr.Default);
}

public static class CharWidth
{
    /// <summary>Columns a code point occupies: 0 (combining, zero-width), 1, or 2 (East Asian wide, most emoji).</summary>
    public static int Of(int rune)
    {
        if (rune < 0x300) return 1;
        if (rune is 0x200B or 0x200C or 0x200D or 0xFE0F or 0xFE0E) return 0;
        if (rune <= 0xFFFF)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory((char)rune);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        }
        return IsWide(rune) ? 2 : 1;
    }

    static bool IsWide(int r) =>
        r is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF
          or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xA000 and <= 0xA4CF
          or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE4F
          or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1F64F
          or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD;
}
