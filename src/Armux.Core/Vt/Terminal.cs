using System.Text;

namespace Armux.Core.Vt;

/// <summary>
/// A terminal screen: the grid of cells, cursor, scroll region, alternate screen and packed scrollback, driven by
/// <see cref="VtParser"/>. It implements the subset of xterm that ConPTY emits and common full-screen programs use.
/// <para>Thread use: <see cref="Feed"/> runs on the reader thread. Readers take <see cref="Sync"/> while reading lines.
/// Events are raised after the lock is released.</para>
/// </summary>
public sealed class Terminal : IVtHandler
{
    readonly VtParser parser;
    readonly OscNotifications notifications = new();
    readonly List<Action> events = [];
    readonly ScrollbackRing scrollback;

    Cell[][] main, alt, screen;
    bool[] mainWrap, altWrap, wrap;
    bool[] tabs;
    int cx, cy, top, bottom;
    bool wrapPending, lineDrawing;
    Attr attr = Attr.Default;
    int lastRune = ' ';
    (int X, int Y, Attr A, bool Pending, bool LineDrawing) savedMain, savedAlt;

    public object Sync { get; } = new();
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorX => cx;
    public int CursorY => cy;
    public bool CursorVisible { get; private set; } = true;
    public bool AutoWrap { get; private set; } = true;
    public bool InsertMode { get; private set; }
    public bool AppCursorKeys { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool FocusReporting { get; private set; }
    public bool AltScreen { get; private set; }
    /// <summary>Mouse reporting the program asked for (DECSET 1000 / 1002 / 1003); None means the terminal selects text.</summary>
    public MouseTracking Mouse { get; private set; }
    /// <summary>DECSET 1006: mouse reports use the SGR encoding.</summary>
    public bool SgrMouse { get; private set; }
    /// <summary>OSC 9;4 progress (ConEmu, Windows Terminal): 0 none, 1 normal, 2 error, 3 indeterminate, 4 paused.</summary>
    public int ProgressState { get; private set; }
    public int ProgressValue { get; private set; }
    public string Title { get; private set; } = "";
    /// <summary>Increases on every change. Renderers compare it to skip work.</summary>
    public long Version { get; private set; }
    /// <summary>Lines in scrollback (0 while the alternate screen is shown, which has none).</summary>
    public int ScrollbackCount => AltScreen ? 0 : scrollback.Count;

    public event Action<string>? TitleChanged;
    /// <summary>OSC 7: the shell's working directory, as a local path.</summary>
    public event Action<string>? CwdChanged;
    public event Action<TermNotification>? Notified;
    public event Action? Bell;
    /// <summary>Bytes the terminal must send back to the program (device status and attribute reports).</summary>
    public event Action<string>? Reply;
    /// <summary>OSC 9;4 changed <see cref="ProgressState"/> or <see cref="ProgressValue"/>.</summary>
    public event Action? ProgressChanged;

    /// <summary>Colors reported to OSC 10 / 11 queries (programs pick light or dark themes from them), as 0xRRGGBB.</summary>
    public uint ReportedForeground { get; set; } = 0xD4D4D4;
    public uint ReportedBackground { get; set; } = 0x262626;

    public Terminal(int cols, int rows, int scrollbackLines = 5000)
    {
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        scrollback = new ScrollbackRing(scrollbackLines);
        main = NewGrid(Cols, Rows); alt = NewGrid(Cols, Rows);
        mainWrap = new bool[Rows]; altWrap = new bool[Rows];
        screen = main; wrap = mainWrap;
        tabs = NewTabs(Cols);
        bottom = Rows - 1;
        parser = new VtParser(this);
    }

    static Cell[][] NewGrid(int cols, int rows)
    {
        var g = new Cell[rows][];
        for (int i = 0; i < rows; i++) g[i] = NewRow(cols);
        return g;
    }

    static Cell[] NewRow(int cols) { var r = new Cell[cols]; Array.Fill(r, Cell.Blank); return r; }
    static bool[] NewTabs(int cols) { var t = new bool[cols]; for (int i = 8; i < cols; i += 8) t[i] = true; return t; }

    // ---- input ---------------------------------------------------------------------------------

    public void Feed(ReadOnlySpan<byte> data)
    {
        lock (Sync)
        {
            parser.Feed(data);
            Version++;
        }
        RaiseEvents();
    }

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    void Later(Action a) { lock (events) events.Add(a); }

    void RaiseEvents()
    {
        Action[] list;
        lock (events) { if (events.Count == 0) return; list = [.. events]; events.Clear(); }
        foreach (var a in list) a();
    }

    // ---- reading (callers hold Sync) -----------------------------------------------------------

    /// <summary>Total lines: scrollback then screen. Line <c>ScrollbackCount + y</c> is screen row y.</summary>
    public int LineCount => ScrollbackCount + Rows;

    /// <summary>Copies line <paramref name="line"/> (see <see cref="LineCount"/>) into <paramref name="dest"/>, padded with blanks.</summary>
    public void ReadLine(int line, Cell[] dest)
    {
        if (line < ScrollbackCount) { scrollback[line].Unpack(dest); return; }
        var row = screen[Math.Clamp(line - ScrollbackCount, 0, Rows - 1)];
        int n = Math.Min(row.Length, dest.Length);
        Array.Copy(row, dest, n);
        if (n < dest.Length) Array.Fill(dest, Cell.Blank, n, dest.Length - n);
    }

    public bool IsWrapped(int line) => line < ScrollbackCount ? scrollback[line].Wrapped : wrap[Math.Clamp(line - ScrollbackCount, 0, Rows - 1)];

    /// <summary>Text from (line1, col1) to (line2, col2) inclusive. Wrapped lines join without a newline; trailing blanks are dropped.</summary>
    public string GetText(int line1, int col1, int line2, int col2)
    {
        var sb = new StringBuilder();
        var buf = new Cell[Cols];
        for (int l = line1; l <= line2; l++)
        {
            ReadLine(l, buf);
            int from = l == line1 ? col1 : 0, to = l == line2 ? col2 : Cols - 1;
            var part = new StringBuilder();
            for (int x = Math.Max(0, from); x <= Math.Min(to, Cols - 1); x++)
            {
                int r = buf[x].Rune;
                if (r == Cell.WideTail) continue;
                part.Append(r == 0 ? " " : char.ConvertFromUtf32(r));
            }
            bool wrapped = IsWrapped(l) && l != line2;
            sb.Append(wrapped ? part.ToString() : part.ToString().TrimEnd());
            if (l != line2 && !wrapped) sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Drops the scrollback (cmux "clear scrollback"); the screen is kept.</summary>
    public void ClearScrollback()
    {
        lock (Sync) { scrollback.Clear(); Version++; }
    }

    // ---- resize --------------------------------------------------------------------------------

    public void Resize(int cols, int rows)
    {
        cols = Math.Max(1, cols); rows = Math.Max(1, rows);
        lock (Sync)
        {
            if (cols == Cols && rows == Rows) return;
            // The main screen and scrollback reflow, as ConPTY expects (it reflows its own buffer the same way and
            // repaints only what differs). The alternate screen is cropped; full-screen programs redraw it.
            if (AltScreen)
            {
                var (sx, sy, sp) = (savedMain.X, savedMain.Y, savedMain.Pending);
                ReflowMain(cols, rows, ref sx, ref sy, ref sp);
                savedMain = (sx, sy, savedMain.A, sp, savedMain.LineDrawing);
                int shift = Math.Max(0, cy - (rows - 1));
                alt = Remap(alt, altWrap, cols, rows, shift, out altWrap);
                cy = Math.Clamp(cy - shift, 0, rows - 1);
                cx = Math.Clamp(cx, 0, cols - 1);
                wrapPending = false;
                screen = alt; wrap = altWrap;
            }
            else
            {
                ReflowMain(cols, rows, ref cx, ref cy, ref wrapPending);
                // The alternate screen must match too: switching to it later clears and draws every row.
                alt = Remap(alt, altWrap, cols, rows, 0, out altWrap);
                screen = main; wrap = mainWrap;
            }
            Cols = cols; Rows = rows;
            top = 0; bottom = rows - 1;
            tabs = NewTabs(cols);
            Version++;
        }
    }

    /// <summary>
    /// Rewraps scrollback and the main screen to <paramref name="cols"/>: wrapped rows join into logical lines, which
    /// are split again at the new width. The cursor keeps its place in its logical line; the bottom of the content
    /// stays at the bottom of the screen, and rows above the screen go to scrollback.
    /// </summary>
    void ReflowMain(int cols, int rows, ref int curX, ref int curY, ref bool pending)
    {
        int oldCols = Cols, sbCount = scrollback.Count;
        // Rows below the cursor that are blank carry nothing to keep.
        int lastRow = Rows - 1;
        while (lastRow > curY && IsBlank(main[lastRow])) lastRow--;

        var logical = new List<List<Cell>>();
        var acc = new List<Cell>();
        int curLine = -1, curOffset = 0;
        var buf = new Cell[oldCols];
        for (int l = 0; l <= sbCount + lastRow; l++)
        {
            bool wrapped;
            Cell[] cells;
            if (l < sbCount) { scrollback[l].Unpack(buf); cells = buf; wrapped = scrollback[l].Wrapped; }
            else { cells = main[l - sbCount]; wrapped = mainWrap[l - sbCount]; }
            int len = wrapped ? oldCols : Trimmed(cells);
            if (l == sbCount + curY)
            {
                curLine = logical.Count;
                curOffset = acc.Count + curX + (pending ? 1 : 0);
                len = Math.Max(len, Math.Min(oldCols, curX + (pending ? 1 : 0)));
            }
            for (int x = 0; x < len; x++) acc.Add(cells[x]);
            if (!wrapped) { logical.Add(acc); acc = []; }
        }
        if (acc.Count > 0) logical.Add(acc);

        var outRows = new List<(Cell[] Row, bool Wrapped)>();
        int newCurRow = 0, newCurCol = 0;
        bool newPending = false;
        for (int i = 0; i < logical.Count; i++)
        {
            var line = logical[i];
            var row = NewRow(cols);
            int col = 0;
            bool found = false;
            for (int k = 0; k < line.Count; k++)
            {
                var c = line[k];
                if (c.Rune == Cell.WideTail) { if (i == curLine && k == curOffset) { newCurRow = outRows.Count; newCurCol = Math.Max(0, col - 1); found = true; } continue; }
                int w = k + 1 < line.Count && line[k + 1].Rune == Cell.WideTail ? 2 : 1;
                if (col + w > cols) { outRows.Add((row, true)); row = NewRow(cols); col = 0; }
                if (i == curLine && k == curOffset) { newCurRow = outRows.Count; newCurCol = col; found = true; }
                row[col] = c;
                if (w == 2 && col + 1 < cols) row[col + 1] = new Cell(Cell.WideTail, c.Attr);
                col += w;
            }
            if (i == curLine && !found)
            {
                // The cursor sits after the line's text.
                int extra = curOffset - line.Count;
                col += extra;
                while (col > cols) { outRows.Add((row, true)); row = NewRow(cols); col -= cols; }
                newCurRow = outRows.Count;
                if (col == cols) { newCurCol = cols - 1; newPending = true; } else newCurCol = col;
            }
            outRows.Add((row, false));
        }
        if (outRows.Count == 0) outRows.Add((NewRow(cols), false));

        int topRow = Math.Max(0, outRows.Count - rows);
        if (newCurRow < topRow) topRow = newCurRow;
        scrollback.Clear();
        for (int i = 0; i < topRow; i++) scrollback.Push(PackedLine.Pack(outRows[i].Row, outRows[i].Wrapped));
        main = new Cell[rows][];
        mainWrap = new bool[rows];
        for (int y = 0; y < rows; y++)
        {
            int src = topRow + y;
            main[y] = src < outRows.Count ? outRows[src].Row : NewRow(cols);
            mainWrap[y] = src < outRows.Count && outRows[src].Wrapped;
        }
        curY = Math.Clamp(newCurRow - topRow, 0, rows - 1);
        curX = Math.Clamp(newCurCol, 0, cols - 1);
        pending = newPending;
    }

    static bool IsBlank(Cell[] row) { foreach (var c in row) if (c != Cell.Blank) return false; return true; }

    static int Trimmed(Cell[] row)
    {
        int n = row.Length;
        while (n > 0 && row[n - 1] == Cell.Blank) n--;
        return n;
    }

    static Cell[][] Remap(Cell[][] old, bool[] oldWrap, int cols, int rows, int skip, out bool[] newWrap)
    {
        var g = new Cell[rows][];
        newWrap = new bool[rows];
        for (int y = 0; y < rows; y++)
        {
            g[y] = NewRow(cols);
            int src = y + skip;
            if (src < old.Length)
            {
                Array.Copy(old[src], g[y], Math.Min(cols, old[src].Length));
                newWrap[y] = oldWrap[src];
            }
        }
        return g;
    }

    // ---- IVtHandler ----------------------------------------------------------------------------

    public void Print(int rune)
    {
        if (lineDrawing && rune is >= 0x5F and <= 0x7E) rune = LineDrawing[rune - 0x5F];
        int w = CharWidth.Of(rune);
        if (w == 0) return;   // combining marks are dropped
        lastRune = rune;
        if (wrapPending && AutoWrap) { wrap[cy] = true; cx = 0; LineFeed(); }
        wrapPending = false;
        if (w == 2 && cx == Cols - 1)
        {
            if (!AutoWrap || Cols < 2) return;
            screen[cy][cx] = Blank();
            wrap[cy] = true; cx = 0; LineFeed();
        }
        var row = screen[cy];
        if (InsertMode) { Array.Copy(row, cx, row, cx + w, Math.Max(0, Cols - cx - w)); }
        ClearWide(row, cx);
        row[cx] = new Cell(rune, attr);
        if (w == 2) { ClearWide(row, cx + 1); row[cx + 1] = new Cell(Cell.WideTail, attr); }
        if (cx + w >= Cols) { cx = Cols - 1; wrapPending = true; }
        else cx += w;
    }

    /// <summary>Overwriting half of a wide character blanks the other half.</summary>
    void ClearWide(Cell[] row, int x)
    {
        if (x >= row.Length) return;
        if (row[x].Rune == Cell.WideTail && x > 0) row[x - 1] = Blank();
        if (x + 1 < row.Length && row[x + 1].Rune == Cell.WideTail) row[x + 1] = Blank();
    }

    public void Execute(int c)
    {
        switch (c)
        {
            case 0x07: Later(() => Bell?.Invoke()); break;
            case 0x08: if (wrapPending) wrapPending = false; else cx = Math.Max(0, cx - 1); break;
            case 0x09: TabForward(1); break;
            case 0x0A: case 0x0B: case 0x0C: LineFeed(); break;
            case 0x0D: cx = 0; wrapPending = false; break;
            case 0x0E: case 0x0F: break;   // shift out / in: G1 is not supported
        }
    }

    public void Esc(string inter, char final)
    {
        if (inter == "(") { lineDrawing = final == '0'; return; }
        if (inter.Length > 0) return;
        switch (final)
        {
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'D': LineFeed(); break;
            case 'E': cx = 0; LineFeed(); break;
            case 'M': ReverseIndex(); break;
            case 'H': tabs[cx] = true; break;
            case 'c': Reset(); break;
        }
    }

    static int Arg(ReadOnlySpan<int> a, int i, int def) => i < a.Length && a[i] > 0 ? a[i] : def;
    static int Raw(ReadOnlySpan<int> a, int i) => i < a.Length ? a[i] : -1;

    public void Csi(ReadOnlySpan<int> a, ReadOnlySpan<bool> sub, char priv, string inter, char final)
    {
        if (priv == '?')
        {
            if (final is 'h' or 'l') for (int i = 0; i < Math.Max(1, a.Length); i++) DecMode(Raw(a, i), final == 'h');
            return;
        }
        if (priv == '>')
        {
            if (final == 'c') Later(() => Reply?.Invoke("\x1b[>0;10;1c"));
            return;
        }
        if (priv != '\0') return;
        if (inter == " " && final == 'q') return;   // DECSCUSR cursor shape: not supported yet
        if (inter.Length > 0) return;

        wrapPending = final is 'm' && wrapPending;   // SGR keeps a pending wrap; cursor and edit commands clear it
        switch (final)
        {
            case '@': InsertChars(Arg(a, 0, 1)); break;
            case 'A': cy = Math.Max(cy < top ? 0 : top, cy - Arg(a, 0, 1)); break;
            case 'B': case 'e': cy = Math.Min(cy > bottom ? Rows - 1 : bottom, cy + Arg(a, 0, 1)); break;
            case 'C': case 'a': cx = Math.Min(Cols - 1, cx + Arg(a, 0, 1)); break;
            case 'D': cx = Math.Max(0, cx - Arg(a, 0, 1)); break;
            case 'E': cx = 0; cy = Math.Min(bottom, cy + Arg(a, 0, 1)); break;
            case 'F': cx = 0; cy = Math.Max(top, cy - Arg(a, 0, 1)); break;
            case 'G': case '`': cx = Math.Clamp(Arg(a, 0, 1) - 1, 0, Cols - 1); break;
            case 'H': case 'f': cy = Math.Clamp(Arg(a, 0, 1) - 1, 0, Rows - 1); cx = Math.Clamp(Arg(a, 1, 1) - 1, 0, Cols - 1); break;
            case 'I': TabForward(Arg(a, 0, 1)); break;
            case 'Z': for (int n = Arg(a, 0, 1); n > 0 && cx > 0; n--) { do cx--; while (cx > 0 && !tabs[cx]); } break;
            case 'J': EraseDisplay(Math.Max(0, Raw(a, 0))); break;
            case 'K': EraseLine(Math.Max(0, Raw(a, 0))); break;
            case 'L': if (cy >= top && cy <= bottom) ScrollDown(Arg(a, 0, 1), cy); break;
            case 'M': if (cy >= top && cy <= bottom) ScrollUp(Arg(a, 0, 1), cy); break;
            case 'P': DeleteChars(Arg(a, 0, 1)); break;
            case 'S': ScrollUp(Arg(a, 0, 1), top); break;
            case 'T': ScrollDown(Arg(a, 0, 1), top); break;
            case 'X': Fill(screen[cy], cx, Math.Min(Cols, cx + Arg(a, 0, 1))); break;
            case 'b': for (int n = Math.Min(Arg(a, 0, 1), 65535); n > 0; n--) Print(lastRune); break;
            case 'c': if (Raw(a, 0) <= 0) Later(() => Reply?.Invoke("\x1b[?1;2c")); break;
            case 'd': cy = Math.Clamp(Arg(a, 0, 1) - 1, 0, Rows - 1); break;
            case 'g': if (Raw(a, 0) <= 0) tabs[cx] = false; else if (Raw(a, 0) == 3) Array.Clear(tabs); break;
            case 'h': case 'l': if (Raw(a, 0) == 4) InsertMode = final == 'h'; break;
            case 'm': Sgr(a, sub); break;
            case 'n':
                if (Raw(a, 0) == 5) Later(() => Reply?.Invoke("\x1b[0n"));
                else if (Raw(a, 0) == 6) { int y = cy + 1, x = cx + 1; Later(() => Reply?.Invoke($"\x1b[{y};{x}R")); }
                break;
            case 'r':
                int t = Arg(a, 0, 1) - 1, b = Arg(a, 1, Rows) - 1;
                if (t < b && b < Rows) { top = t; bottom = b; cx = 0; cy = 0; }
                break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
        }
    }

    public void Osc(string data)
    {
        int semi = data.IndexOf(';');
        string code = semi < 0 ? data : data[..semi], rest = semi < 0 ? "" : data[(semi + 1)..];
        switch (code)
        {
            case "0": case "2":
                Title = rest;
                Later(() => TitleChanged?.Invoke(rest));
                break;
            case "7":
                if (FileUrlToPath(rest) is { } path) Later(() => CwdChanged?.Invoke(path));
                break;
            case "9" when rest.StartsWith("4;", StringComparison.Ordinal) || rest == "4":
                Progress(rest);
                break;
            case "9": case "99": case "777":
                if (notifications.Handle(data) is { } n) Later(() => Notified?.Invoke(n));
                break;
            case "10" when rest == "?":
                Later(() => Reply?.Invoke($"\x1b]10;{XColor(ReportedForeground)}\x1b\\"));
                break;
            case "11" when rest == "?":
                Later(() => Reply?.Invoke($"\x1b]11;{XColor(ReportedBackground)}\x1b\\"));
                break;
            // OSC 8 hyperlinks, OSC 52 clipboard (not allowed: programs could read or replace it) and others are ignored.
        }
    }

    static string XColor(uint rgb) => $"rgb:{(rgb >> 16) & 0xFF:x2}{(rgb >> 16) & 0xFF:x2}/{(rgb >> 8) & 0xFF:x2}{(rgb >> 8) & 0xFF:x2}/{rgb & 0xFF:x2}{rgb & 0xFF:x2}";

    /// <summary>OSC 9;4;state;value. State 0 clears; value is a percentage.</summary>
    void Progress(string rest)
    {
        var parts = rest.Split(';');
        int state = parts.Length > 1 && int.TryParse(parts[1], out var st) ? Math.Clamp(st, 0, 4) : 0;
        int value = parts.Length > 2 && int.TryParse(parts[2], out var v) ? Math.Clamp(v, 0, 100) : 0;
        if (state == ProgressState && value == ProgressValue) return;
        ProgressState = state;
        ProgressValue = value;
        Later(() => ProgressChanged?.Invoke());
    }

    /// <summary>"file://host/C:/dir" or "file:///C:/dir" to "C:\dir". Null for anything else.</summary>
    public static string? FileUrlToPath(string url)
    {
        if (!url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return null;
        int slash = url.IndexOf('/', 7);
        if (slash < 0) return null;
        string p = Uri.UnescapeDataString(url[slash..]);
        if (p.Length >= 3 && p[0] == '/' && char.IsAsciiLetter(p[1]) && p[2] == ':') p = p[1..];
        return p.Replace('/', '\\');
    }

    // ---- operations ----------------------------------------------------------------------------

    Cell Blank() => new(0, new Attr(VtColor.Default, attr.Bg, CellFlags.None));

    void Fill(Cell[] row, int from, int to)
    {
        var b = Blank();
        for (int x = Math.Max(0, from); x < Math.Min(to, row.Length); x++) row[x] = b;
    }

    void LineFeed()
    {
        wrapPending = false;
        if (cy == bottom) ScrollUp(1, top, toScrollback: true);
        else if (cy < Rows - 1) cy++;
    }

    void ReverseIndex()
    {
        wrapPending = false;
        if (cy == top) ScrollDown(1, top);
        else if (cy > 0) cy--;
    }

    /// <summary>Scrolls rows <paramref name="from"/>..bottom up by n. A line feed at the bottom moves the top line of the main screen to scrollback.</summary>
    void ScrollUp(int n, int from, bool toScrollback = false)
    {
        n = Math.Min(n, bottom - from + 1);
        for (int i = 0; i < n; i++)
        {
            var gone = screen[from];
            if (toScrollback && !AltScreen && from == 0) scrollback.Push(PackedLine.Pack(gone, wrap[from]));
            for (int y = from; y < bottom; y++) { screen[y] = screen[y + 1]; wrap[y] = wrap[y + 1]; }
            screen[bottom] = gone;
            Fill(gone, 0, Cols);
            wrap[bottom] = false;
        }
    }

    void ScrollDown(int n, int from)
    {
        n = Math.Min(n, bottom - from + 1);
        for (int i = 0; i < n; i++)
        {
            var gone = screen[bottom];
            for (int y = bottom; y > from; y--) { screen[y] = screen[y - 1]; wrap[y] = wrap[y - 1]; }
            screen[from] = gone;
            Fill(gone, 0, Cols);
            wrap[from] = false;
        }
    }

    void InsertChars(int n)
    {
        var row = screen[cy];
        n = Math.Min(n, Cols - cx);
        Array.Copy(row, cx, row, cx + n, Cols - cx - n);
        Fill(row, cx, cx + n);
    }

    void DeleteChars(int n)
    {
        var row = screen[cy];
        n = Math.Min(n, Cols - cx);
        Array.Copy(row, cx + n, row, cx, Cols - cx - n);
        Fill(row, Cols - n, Cols);
    }

    void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                Fill(screen[cy], cx, Cols);
                for (int y = cy + 1; y < Rows; y++) { Fill(screen[y], 0, Cols); wrap[y] = false; }
                break;
            case 1:
                for (int y = 0; y < cy; y++) { Fill(screen[y], 0, Cols); wrap[y] = false; }
                Fill(screen[cy], 0, cx + 1);
                break;
            case 2:
                for (int y = 0; y < Rows; y++) { Fill(screen[y], 0, Cols); wrap[y] = false; }
                break;
            case 3:
                scrollback.Clear();
                break;
        }
    }

    void EraseLine(int mode)
    {
        var row = screen[cy];
        switch (mode)
        {
            case 0: Fill(row, cx, Cols); wrap[cy] = false; break;
            case 1: Fill(row, 0, cx + 1); break;
            case 2: Fill(row, 0, Cols); wrap[cy] = false; break;
        }
    }

    void TabForward(int n)
    {
        for (; n > 0 && cx < Cols - 1; n--) { do cx++; while (cx < Cols - 1 && !tabs[cx]); }
    }

    void SaveCursor()
    {
        var s = (cx, cy, attr, wrapPending, lineDrawing);
        if (AltScreen) savedAlt = s; else savedMain = s;
    }

    void RestoreCursor()
    {
        var s = AltScreen ? savedAlt : savedMain;
        cx = Math.Clamp(s.X, 0, Cols - 1);
        cy = Math.Clamp(s.Y, 0, Rows - 1);
        attr = s.A;
        wrapPending = s.Pending;
        lineDrawing = s.LineDrawing;
    }

    void DecMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1: AppCursorKeys = on; break;
            case 7: AutoWrap = on; break;
            case 25: CursorVisible = on; break;
            case 47: case 1047: SetAltScreen(on, clear: mode == 1047 && on); break;
            case 1048: if (on) SaveCursor(); else RestoreCursor(); break;
            case 1049:
                if (on) { SaveCursor(); SetAltScreen(true, clear: true); }
                else { SetAltScreen(false, clear: false); RestoreCursor(); }
                break;
            case 1004: FocusReporting = on; break;
            case 1000: Mouse = on ? MouseTracking.Click : MouseTracking.None; break;
            case 1002: Mouse = on ? MouseTracking.Drag : MouseTracking.None; break;
            case 1003: Mouse = on ? MouseTracking.Any : MouseTracking.None; break;
            case 1006: SgrMouse = on; break;
            case 2004: BracketedPaste = on; break;
            // 9001 (win32-input-mode, requested by ConPTY), 1005/1015 mouse encodings and 2026 (synchronized output) are not supported.
        }
    }

    void SetAltScreen(bool on, bool clear)
    {
        if (on == AltScreen) return;
        AltScreen = on;
        screen = on ? alt : main;
        wrap = on ? altWrap : mainWrap;
        if (on && clear) for (int y = 0; y < Rows; y++) { Fill(alt[y], 0, Cols); altWrap[y] = false; }
        top = 0; bottom = Rows - 1;
    }

    void Reset()
    {
        SetAltScreen(false, false);
        attr = Attr.Default;
        for (int y = 0; y < Rows; y++) { Fill(main[y], 0, Cols); mainWrap[y] = false; }
        scrollback.Clear();
        cx = cy = 0; top = 0; bottom = Rows - 1;
        wrapPending = lineDrawing = InsertMode = AppCursorKeys = BracketedPaste = FocusReporting = SgrMouse = false;
        Mouse = MouseTracking.None;
        AutoWrap = CursorVisible = true;
        tabs = NewTabs(Cols);
    }

    void Sgr(ReadOnlySpan<int> a, ReadOnlySpan<bool> sub)
    {
        if (a.Length == 0) { attr = Attr.Default; return; }
        uint fg = attr.Fg, bg = attr.Bg;
        var f = attr.Flags;
        for (int i = 0; i < a.Length; i++)
        {
            int p = Math.Max(0, a[i]);
            switch (p)
            {
                case 0: fg = bg = VtColor.Default; f = CellFlags.None; break;
                case 1: f |= CellFlags.Bold; break;
                case 2: f |= CellFlags.Dim; break;
                case 3: f |= CellFlags.Italic; break;
                case 4:
                    // 4:0 turns underline off; other 4:n styles are drawn as a plain underline.
                    if (i + 1 < a.Length && sub[i + 1]) { f = a[i + 1] == 0 ? f & ~CellFlags.Underline : f | CellFlags.Underline; i++; }
                    else f |= CellFlags.Underline;
                    break;
                case 5: case 6: f |= CellFlags.Blink; break;
                case 7: f |= CellFlags.Inverse; break;
                case 8: f |= CellFlags.Hidden; break;
                case 9: f |= CellFlags.Strike; break;
                case 21: f |= CellFlags.Underline; break;
                case 22: f &= ~(CellFlags.Bold | CellFlags.Dim); break;
                case 23: f &= ~CellFlags.Italic; break;
                case 24: f &= ~CellFlags.Underline; break;
                case 25: f &= ~CellFlags.Blink; break;
                case 27: f &= ~CellFlags.Inverse; break;
                case 28: f &= ~CellFlags.Hidden; break;
                case 29: f &= ~CellFlags.Strike; break;
                case >= 30 and <= 37: fg = VtColor.Indexed(p - 30); break;
                case 38: fg = ExtendedColor(a, sub, ref i, fg); break;
                case 39: fg = VtColor.Default; break;
                case >= 40 and <= 47: bg = VtColor.Indexed(p - 40); break;
                case 48: bg = ExtendedColor(a, sub, ref i, bg); break;
                case 49: bg = VtColor.Default; break;
                case 58: ExtendedColor(a, sub, ref i, 0); break;   // underline color: parsed and dropped
                case >= 90 and <= 97: fg = VtColor.Indexed(p - 90 + 8); break;
                case >= 100 and <= 107: bg = VtColor.Indexed(p - 100 + 8); break;
            }
        }
        attr = new Attr(fg, bg, f);
    }

    /// <summary>Parses "38;5;n", "38;2;r;g;b", "38:5:n", "38:2:r:g:b" and "38:2::r:g:b". Advances <paramref name="i"/> past it.</summary>
    static uint ExtendedColor(ReadOnlySpan<int> a, ReadOnlySpan<bool> sub, ref int i, uint current)
    {
        if (i + 1 >= a.Length) return current;
        bool colon = sub[i + 1];
        int mode = a[i + 1];
        if (mode == 5 && i + 2 < a.Length) { int idx = a[i + 2]; i += 2; return idx is >= 0 and <= 255 ? VtColor.Indexed(idx) : current; }
        if (mode != 2) { i++; return current; }
        int start = i + 2;
        int count = 0;
        if (colon) while (start + count < a.Length && sub[start + count]) count++;
        else count = Math.Min(3, a.Length - start);
        if (count < 3) { i = start + count - 1; return current; }
        int s = start + count - 3;   // with a color-space id, the last three are r, g, b
        uint c = VtColor.Rgb(Math.Max(0, a[s]), Math.Max(0, a[s + 1]), Math.Max(0, a[s + 2]));
        i = start + count - 1;
        return c;
    }

    /// <summary>DEC special graphics for 0x5F-0x7E (ESC ( 0), used for line drawing.</summary>
    static readonly int[] LineDrawing =
    [
        ' ', '◆', '▒', '␉', '␌', '␍', '␊', '°', '±', '␤', '␋', '┘', '┐', '┌', '└', '┼',
        '⎺', '⎻', '─', '⎼', '⎽', '├', '┤', '┴', '┬', '│', '≤', '≥', 'π', '≠', '£', '·',
    ];
}

/// <summary>Mouse reporting modes, from least to most events.</summary>
public enum MouseTracking { None, Click, Drag, Any }

/// <summary>A scrollback line stored compactly: its text plus the columns where the attributes change.</summary>
public readonly record struct PackedLine(string Text, (int Col, Attr Attr)[] Runs, bool Wrapped)
{
    public static PackedLine Pack(Cell[] row, bool wrapped)
    {
        int last = row.Length - 1;
        while (last >= 0 && row[last] == Cell.Blank) last--;
        var sb = new StringBuilder(last + 1);
        var runs = new List<(int, Attr)>();
        Attr prev = Attr.Default;
        for (int x = 0; x <= last; x++)
        {
            var c = row[x];
            if (c.Rune == Cell.WideTail) continue;
            if (c.Attr != prev) { runs.Add((x, c.Attr)); prev = c.Attr; }
            sb.Append(c.Rune <= 0 ? " " : char.ConvertFromUtf32(c.Rune));
        }
        return new PackedLine(sb.ToString(), [.. runs], wrapped);
    }

    public void Unpack(Cell[] dest)
    {
        Array.Fill(dest, Cell.Blank);
        int col = 0, run = 0;
        Attr a = Attr.Default;
        for (int i = 0; i < Text.Length && col < dest.Length; i++)
        {
            int r = Text[i];
            if (char.IsHighSurrogate(Text, i) && i + 1 < Text.Length) { r = char.ConvertToUtf32(Text[i], Text[i + 1]); i++; }
            while (run < Runs.Length && Runs[run].Col <= col) a = Runs[run++].Attr;
            int w = r == ' ' ? 1 : CharWidth.Of(r);
            if (w == 0) continue;
            dest[col] = new Cell(r == ' ' ? 0 : r, a);
            if (w == 2 && col + 1 < dest.Length) dest[col + 1] = new Cell(Cell.WideTail, a);
            col += w;
        }
    }
}

/// <summary>Fixed-capacity ring of scrollback lines; the oldest line is dropped when full.</summary>
sealed class ScrollbackRing(int capacity)
{
    readonly PackedLine[] lines = new PackedLine[Math.Max(0, capacity)];
    int start;
    public int Count { get; private set; }

    public PackedLine this[int i] => lines[(start + i) % lines.Length];

    public void Push(PackedLine line)
    {
        if (lines.Length == 0) return;
        if (Count < lines.Length) lines[(start + Count++) % lines.Length] = line;
        else { lines[start] = line; start = (start + 1) % lines.Length; }
    }

    public void Clear() { Array.Clear(lines); start = 0; Count = 0; }
}
