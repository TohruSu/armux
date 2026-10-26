using System.Text;

namespace Armux.Core.Vt;

/// <summary>Receives the actions of <see cref="VtParser"/>.</summary>
public interface IVtHandler
{
    void Print(int rune);
    /// <summary>A C0 control (BEL, BS, HT, LF, VT, FF, CR, ...).</summary>
    void Execute(int control);
    /// <summary>
    /// <c>CSI [private] params [intermediates] final</c>. Missing parameters are -1. <paramref name="sub"/> marks
    /// parameters that followed a ':' (SGR 38:2::r:g:b).
    /// </summary>
    void Csi(ReadOnlySpan<int> args, ReadOnlySpan<bool> sub, char privateMarker, string intermediates, char final);
    void Esc(string intermediates, char final);
    void Osc(string data);
}

/// <summary>
/// A VT500-style escape sequence parser (the state machine described by Paul Williams, simplified): UTF-8 in,
/// print / execute / CSI / ESC / OSC actions out. DCS, SOS, PM and APC strings are consumed and ignored.
/// </summary>
public sealed class VtParser(IVtHandler handler)
{
    enum State { Ground, Escape, EscapeIntermediate, CsiEntry, CsiParam, CsiIntermediate, CsiIgnore, OscString, IgnoreString }

    const int MaxParams = 32, MaxOsc = 64 * 1024;

    readonly Decoder utf8 = new UTF8Encoding(false).GetDecoder();
    readonly int[] args = new int[MaxParams];
    readonly bool[] sub = new bool[MaxParams];
    readonly StringBuilder intermediates = new();
    readonly StringBuilder osc = new();
    char[] chars = new char[4096];
    State state;
    int argCount;
    char privateMarker;
    bool ignoreStringEsc;   // ESC seen inside an ignored string (possible ST)
    char highSurrogate;

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (chars.Length < data.Length + 1) chars = new char[data.Length + 1];
        int n = utf8.GetChars(data, chars, flush: false);
        for (int i = 0; i < n; i++)
        {
            char c = chars[i];
            if (char.IsHighSurrogate(c)) { highSurrogate = c; continue; }
            int rune = char.IsLowSurrogate(c) && highSurrogate != 0 ? char.ConvertToUtf32(highSurrogate, c) : c;
            highSurrogate = '\0';
            Step(rune);
        }
    }

    void Step(int c)
    {
        // "Anywhere" transitions.
        if (c == 0x1B && state != State.OscString && state != State.IgnoreString) { Enter(State.Escape); return; }
        if (c is 0x18 or 0x1A) { handler.Execute(c); Enter(State.Ground); return; }

        switch (state)
        {
            case State.Ground:
                if (c < 0x20 || c == 0x7F) { if (c != 0x7F) handler.Execute(c); }
                else if (c is >= 0x80 and <= 0x9F) { }   // C1 controls are not used in UTF-8 mode
                else handler.Print(c);
                break;

            case State.Escape:
                if (c < 0x20) handler.Execute(c);
                else if (c is >= 0x20 and <= 0x2F) { intermediates.Append((char)c); state = State.EscapeIntermediate; }
                else if (c == '[') Enter(State.CsiEntry);
                else if (c == ']') Enter(State.OscString);
                else if (c is 'P' or 'X' or '^' or '_') Enter(State.IgnoreString);
                else if (c is >= 0x30 and <= 0x7E) { handler.Esc(intermediates.ToString(), (char)c); Enter(State.Ground); }
                break;

            case State.EscapeIntermediate:
                if (c < 0x20) handler.Execute(c);
                else if (c is >= 0x20 and <= 0x2F) intermediates.Append((char)c);
                else if (c is >= 0x30 and <= 0x7E) { handler.Esc(intermediates.ToString(), (char)c); Enter(State.Ground); }
                break;

            case State.CsiEntry:
            case State.CsiParam:
                if (c < 0x20) handler.Execute(c);
                else if (c is >= '0' and <= '9')
                {
                    if (argCount == 0) argCount = 1;
                    ref int a = ref args[argCount - 1];
                    a = a < 0 ? c - '0' : Math.Min(a * 10 + (c - '0'), 1_000_000);
                    state = State.CsiParam;
                }
                else if (c is ';' or ':')
                {
                    if (argCount == 0) argCount = 1;
                    if (argCount < MaxParams) { args[argCount] = -1; sub[argCount] = c == ':'; argCount++; }
                    state = State.CsiParam;
                }
                else if (c is '<' or '=' or '>' or '?')
                {
                    if (state == State.CsiEntry) privateMarker = (char)c; else state = State.CsiIgnore;
                }
                else if (c is >= 0x20 and <= 0x2F) { intermediates.Append((char)c); state = State.CsiIntermediate; }
                else if (c is >= 0x40 and <= 0x7E) { DispatchCsi((char)c); }
                break;

            case State.CsiIntermediate:
                if (c < 0x20) handler.Execute(c);
                else if (c is >= 0x20 and <= 0x2F) intermediates.Append((char)c);
                else if (c is >= 0x30 and <= 0x3F) state = State.CsiIgnore;
                else if (c is >= 0x40 and <= 0x7E) DispatchCsi((char)c);
                break;

            case State.CsiIgnore:
                if (c < 0x20) handler.Execute(c);
                else if (c is >= 0x40 and <= 0x7E) Enter(State.Ground);
                break;

            case State.OscString:
                if (c == 0x07) { handler.Osc(osc.ToString()); Enter(State.Ground); }
                else if (c == 0x1B) { handler.Osc(osc.ToString()); Enter(State.Escape); }   // ST is ESC '\'; the '\' is a no-op ESC dispatch
                else if (c >= 0x20 && osc.Length < MaxOsc) osc.Append(char.ConvertFromUtf32(c));
                break;

            case State.IgnoreString:
                if (c == 0x07) Enter(State.Ground);
                else if (c == 0x1B) ignoreStringEsc = true;
                else if (ignoreStringEsc) { ignoreStringEsc = false; if (c == '\\') Enter(State.Ground); }
                break;
        }
    }

    void DispatchCsi(char final)
    {
        handler.Csi(args.AsSpan(0, argCount), sub.AsSpan(0, argCount), privateMarker, intermediates.ToString(), final);
        Enter(State.Ground);
    }

    void Enter(State s)
    {
        state = s;
        if (s is State.Escape or State.CsiEntry)
        {
            intermediates.Clear();
            argCount = 0;
            privateMarker = '\0';
            Array.Fill(args, -1);
            Array.Clear(sub);
        }
        if (s == State.OscString) osc.Clear();
        if (s == State.IgnoreString) ignoreStringEsc = false;
    }
}
