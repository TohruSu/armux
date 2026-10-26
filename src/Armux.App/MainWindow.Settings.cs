using Armux.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Armux.App;

/// <summary>Settings (cmux ⌘,): terminal font and size, and the UI font. Saved to %LOCALAPPDATA%\armux\settings.json.</summary>
public sealed partial class MainWindow
{
    public static string SettingsPath =>
        Environment.GetEnvironmentVariable("ARMUX_SETTINGS_FILE") is { Length: > 0 } p ? p
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "armux", "settings.json");

    const string Sample = "PS ~\\dev\\armux> git status  -> => != 0O 1lI {}[]";

    void ApplySettings(Settings s)
    {
        Theme.UseSettings(s);
        try { Theme.Settings.Save(SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { App.Log(nameof(ApplySettings), e); }
        palette.UpdateFont();
        RenderAll();
    }

    void ChangeFontSize(double delta) =>
        ApplySettings(Theme.Settings.WithTerminalFontSize(delta == 0 ? Settings.DefaultTerminalFontSize : Theme.TerminalFontSize + delta));

    async Task OpenSettingsAsync()
    {
        var showAll = new CheckBox { Content = "Show all fonts, not only monospace" };
        var terminalFont = FontBox("Terminal font");
        // A plain stepper: NumberBox crashes NativeAOT builds (missing TabViewScrollButtonBackground resource).
        double size = Theme.TerminalFontSize;
        var sizeText = Ui.Text("", 13, Theme.Brush("Text"));
        sizeText.Width = 56;
        sizeText.TextAlignment = TextAlignment.Center;
        sizeText.VerticalAlignment = VerticalAlignment.Center;
        var smaller = new Button { Content = "−", Width = 36 };
        var larger = new Button { Content = "+", Width = 36 };
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        sizeRow.Children.Add(smaller);
        sizeRow.Children.Add(sizeText);
        sizeRow.Children.Add(larger);
        var sizeBox = new StackPanel { Spacing = 6 };
        sizeBox.Children.Add(Ui.Text("Terminal font size", 14, Theme.Brush("Text")));
        sizeBox.Children.Add(sizeRow);
        var uiFont = FontBox("Interface font");
        var preview = new TextBlock { Text = Sample, Foreground = Theme.Term(TermColor.Default), TextWrapping = TextWrapping.Wrap };
        var previewBox = new Border { Child = preview, Background = Theme.Brush("Pane"), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(6), BorderBrush = Theme.Brush("Border"), BorderThickness = new Thickness(1) };
        var note = Ui.Text("", 12, Theme.Brush("TextDim"));
        note.TextWrapping = TextWrapping.Wrap;

        // The current value is selected as an item; setting only Text leaves an editable ComboBox blank.
        void Fill(ComboBox box, IEnumerable<string> names, string current)
        {
            var list = new List<string>();
            if (box == terminalFont) list.Add(Settings.DefaultTerminalFont);
            list.AddRange(names.Where(n => !list.Contains(n, StringComparer.OrdinalIgnoreCase)));
            if (current.Length > 0 && !list.Contains(current, StringComparer.OrdinalIgnoreCase)) list.Insert(0, current);
            box.Items.Clear();
            foreach (var n in list) box.Items.Add(n);
            box.SelectedIndex = list.FindIndex(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        }
        string Chosen(ComboBox box) => (box.Text is { Length: > 0 } t ? t : box.SelectedItem as string ?? "").Trim();
        void Update()
        {
            var family = Chosen(terminalFont);
            preview.FontFamily = new FontFamily(Theme.FontSource(family) + ", Cascadia Mono, Consolas");
            preview.FontSize = size;
            sizeText.Text = $"{size:0.#} pt";
            bool bundled = family.Equals(Settings.DefaultTerminalFont, StringComparison.OrdinalIgnoreCase);
            note.Text = family.Length == 0 ? "" : bundled ? "Lilex is Zed's default font and ships with armux."
                : FontList.IsInstalled(family) ? "" : $"\"{family}\" is not installed. Terminals fall back to Cascadia Mono.";
        }

        Fill(terminalFont, FontList.Monospace, Theme.Settings.TerminalFont);
        Fill(uiFont, FontList.All, Theme.Settings.UiFont);
        smaller.Click += (_, _) => { size = Math.Max(Settings.MinFontSize, size - 1); Update(); };
        larger.Click += (_, _) => { size = Math.Min(Settings.MaxFontSize, size + 1); Update(); };
        showAll.Checked += (_, _) => Fill(terminalFont, FontList.All, Chosen(terminalFont));
        showAll.Unchecked += (_, _) => Fill(terminalFont, FontList.Monospace, Chosen(terminalFont));
        terminalFont.SelectionChanged += (_, _) => { if (terminalFont.SelectedItem is string n) terminalFont.Text = n; Update(); };
        terminalFont.TextSubmitted += (_, _) => DispatcherQueue.TryEnqueue(Update);
        Update();

        var body = new StackPanel { Spacing = 12, Width = 440 };
        body.Children.Add(terminalFont);
        body.Children.Add(showAll);
        body.Children.Add(sizeBox);
        body.Children.Add(previewBox);
        body.Children.Add(note);
        body.Children.Add(uiFont);

        var dlg = Dialog("Settings", new ScrollViewer { Content = body, MaxHeight = 560 }, "Save");
        dlg.SecondaryButtonText = "Reset to defaults";
        dlg.SecondaryButtonClick += (_, a) =>
        {
            a.Cancel = true;
            var d = new Settings();
            Fill(terminalFont, showAll.IsChecked == true ? FontList.All : FontList.Monospace, d.TerminalFont);
            Fill(uiFont, FontList.All, d.UiFont);
            size = d.TerminalFontSize;
            Update();
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        ApplySettings(new Settings { TerminalFont = Chosen(terminalFont), TerminalFontSize = size, UiFont = Chosen(uiFont) });
    }

    static ComboBox FontBox(string header) => new()
    {
        Header = header,
        IsEditable = true,
        Width = 320,
        MaxDropDownHeight = 360,
        IsTextSearchEnabled = true,
    };
}
