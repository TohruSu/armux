using System.Diagnostics;
using System.Text;
using Armux.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Armux.App;

/// <summary>
/// Read-only file panel opened from the explorer, in place of Warp's code editor (deviation D-17).
/// Shows the first <see cref="MaxBytes"/> of a text file in the terminal font; binary files get an open button.
/// </summary>
public sealed partial class FileView : Grid
{
    const int MaxBytes = 256 * 1024;

    public FileView(Surface surface)
    {
        Background = Theme.Brush("Pane");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var head = new Grid { Padding = new Thickness(12, 4, 6, 4), ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var path = Ui.Text(surface.FilePath, 11.5, Theme.Brush("TextFaint"));
        path.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(path);
        var open = new HyperlinkButton { Content = "Open with default app", FontSize = 11.5, Padding = new Thickness(6, 2, 6, 2) };
        open.Click += (_, _) => OpenExternally(surface.FilePath);
        SetColumn(open, 1);
        head.Children.Add(open);
        Children.Add(head);

        var (text, note) = Read(surface.FilePath);
        var scroll = new ScrollViewer
        {
            Padding = new Thickness(12, 4, 12, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
        };
        var stack = new StackPanel { Spacing = 6 };
        if (note is not null) stack.Children.Add(Ui.Text(note, 12, Theme.Brush("TextDim")));
        if (text is not null)
            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = Theme.TerminalFont,
                FontSize = Theme.TerminalFontSize,
                LineHeight = Math.Round(Theme.TerminalFontSize * Theme.Settings.LineHeight),
                Foreground = Theme.Term(TermColor.Default),
                IsTextSelectionEnabled = true,
            });
        scroll.Content = stack;
        SetRow(scroll, 1);
        Children.Add(scroll);
    }

    /// <summary>The text to show and an optional note (truncated, binary, unreadable).</summary>
    static (string? Text, string? Note) Read(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var buf = new byte[(int)Math.Min(f.Length, MaxBytes)];
            f.ReadExactly(buf);
            if (Array.IndexOf(buf, (byte)0, 0, Math.Min(buf.Length, 8192)) >= 0)
                return (null, "Binary file. Use \"Open with default app\".");
            var text = Encoding.UTF8.GetString(buf).Replace("\t", "    ");
            return (text, f.Length > MaxBytes ? $"Showing the first {MaxBytes / 1024} KB of {f.Length / 1024} KB." : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, $"Can't read this file: {e.Message}");
        }
    }

    public static void OpenExternally(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { App.Log(nameof(OpenExternally), e); }
    }
}
