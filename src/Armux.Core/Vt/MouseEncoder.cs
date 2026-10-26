namespace Armux.Core.Vt;

public enum MouseAction { Press, Release, Move, WheelUp, WheelDown }

/// <summary>
/// xterm mouse reports for programs that turned mouse tracking on (Claude Code, vim, htop): SGR (DECSET 1006)
/// <c>CSI &lt; b ; x ; y M/m</c>, or the legacy X10 bytes <c>CSI M b x y</c> limited to column and row 223.
/// </summary>
public static class MouseEncoder
{
    /// <summary>
    /// The report for an event at 0-based <paramref name="col"/> and <paramref name="row"/>, or null when the
    /// current tracking mode does not report it. <paramref name="button"/> is 0 left, 1 middle, 2 right, or -1 for
    /// a move with no button held.
    /// </summary>
    public static string? Encode(MouseTracking mode, bool sgr, MouseAction action, int button, int col, int row, bool shift, bool alt, bool ctrl)
    {
        if (mode == MouseTracking.None) return null;
        int code;
        switch (action)
        {
            case MouseAction.Press: case MouseAction.Release:
                if (button is < 0 or > 2) return null;
                code = button;
                break;
            case MouseAction.Move:
                if (button >= 0 ? mode < MouseTracking.Drag : mode < MouseTracking.Any) return null;
                code = (button >= 0 ? button : 3) + 32;
                break;
            case MouseAction.WheelUp: code = 64; break;
            case MouseAction.WheelDown: code = 65; break;
            default: return null;
        }
        code += (shift ? 4 : 0) + (alt ? 8 : 0) + (ctrl ? 16 : 0);
        int x = col + 1, y = row + 1;
        if (sgr) return $"\u001b[<{code};{x};{y}{(action == MouseAction.Release ? 'm' : 'M')}";
        if (action == MouseAction.Release) code = 3 + (code & ~3);   // X10 has no button number on release
        if (x > 223 || y > 223) return null;
        return $"\u001b[M{(char)(32 + code)}{(char)(32 + x)}{(char)(32 + y)}";
    }
}
