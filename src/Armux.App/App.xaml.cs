using Microsoft.UI.Xaml;

namespace Armux.App;

public partial class App : Application
{
    MainWindow? window;

    public App()
    {
        // WebView2 must use a short user-data path; a long one broke start-up in the Phase 2 spike.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "armux", "WebView2"));
        InitializeComponent();
        UnhandledException += (_, e) => Log("unhandled", e.Exception);
    }

    /// <summary>Appends an error to %LOCALAPPDATA%\armux\armux.log.</summary>
    public static void Log(string where, Exception ex)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "armux");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "armux.log"), $"{DateTime.Now:O} {where}: {ex}{Environment.NewLine}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(LaunchOptions.Parse(Environment.GetCommandLineArgs()));
        window.Activate();
    }
}
