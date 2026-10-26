using Armux.Core;

namespace Armux.App;

/// <summary>
/// Command-line switches for mock scenarios. They make screenshots and memory measurements reproducible.
///   --scenario demo|idle|panes|browser   (default demo)
///   --panes N            terminal panes for the "panes" scenario
///   --close-after MS     close browser panels and the explorer after MS
///   --theme dark|light   --select N   --palette   --notifications   --zoom   --menu   --colors   --size WxH
///   --explorer           open the file explorer      --settings   open the settings dialog
/// </summary>
public sealed record LaunchOptions(
    string Scenario, int Panes, int CloseAfterMs, string? Theme, int Select,
    bool Palette, bool Notifications, bool Zoom, bool Menu, bool Colors, int Width, int Height,
    bool Explorer = false, bool Settings = false)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? Value(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool Flag(string name) => args.Contains(name);
        var size = (Value("--size") ?? "1440x900").Split('x');
        return new LaunchOptions(
            Value("--scenario") ?? "demo",
            int.TryParse(Value("--panes"), out var p) ? p : 1,
            int.TryParse(Value("--close-after"), out var c) ? c : -1,
            Value("--theme"),
            int.TryParse(Value("--select"), out var s) ? s : 0,
            Flag("--palette"), Flag("--notifications"), Flag("--zoom"), Flag("--menu"), Flag("--colors"),
            int.Parse(size[0]), int.Parse(size[1]),
            Flag("--explorer"), Flag("--settings"));
    }

    public AppState BuildState() => Scenario switch
    {
        "idle" => MockData.Idle(),
        "panes" => MockData.Panes(Math.Max(1, Panes)),
        "browser" => WithBrowser(MockData.Idle()),
        _ => MockData.Demo(),
    };

    static AppState WithBrowser(AppState state)
    {
        var ws = state.Workspaces[0];
        Layout.Split(ws, ws.Focused!, SplitDirection.Right, MockData.Browser());
        return state;
    }
}
