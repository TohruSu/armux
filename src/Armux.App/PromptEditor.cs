using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Armux.App;

/// <summary>
/// Prompt editor (Warp's input editor, Ctrl+G; deviation D-23): an ordinary multi-line text box under a terminal for
/// writing a long prompt with the mouse, selection, undo and spell check, then sending it in one paste. Each terminal
/// keeps its own draft while the editor is closed.
/// </summary>
public sealed partial class PromptEditor : Grid
{
    readonly TextBox box;

    /// <summary>Text to send, and whether Enter follows it (Send) or not (Insert).</summary>
    public event Action<string, bool>? Submitted;
    public event Action? CloseRequested;

    public PromptEditor()
    {
        Background = Theme.Brush("TabBar");
        BorderBrush = Theme.Brush("Divider");
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(10, 6, 10, 10);
        RowSpacing = 6;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid { ColumnSpacing = 6 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = Ui.Text("Prompt editor   Ctrl+Enter send · Shift+Enter insert without Enter · Esc close", 12, Theme.Brush("TextDim"));
        hint.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var insert = new Button { Content = "Insert", Padding = new Thickness(10, 3, 10, 4) };
        insert.Click += (_, _) => Submit(enter: false);
        var send = new Button { Content = "Send", Padding = new Thickness(12, 3, 12, 4), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        send.Click += (_, _) => Submit(enter: true);
        buttons.Children.Add(insert);
        buttons.Children.Add(send);
        buttons.Children.Add(Ui.IconButton(Ui.Glyph(Ui.Close, 10, Theme.Brush("TextDim")), "Close (Esc or Ctrl+G)", () => CloseRequested?.Invoke(), 26));
        SetColumn(buttons, 1);
        head.Children.Add(buttons);
        Children.Add(head);

        box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = true,
            MinHeight = 96,
            MaxHeight = 320,
            FontFamily = new FontFamily(Theme.UiFont),
            FontSize = 14,
            PlaceholderText = "Write a prompt…",
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        // The default TextBox turns white-ish when focused; keep the editor's own color in every state.
        var bg = Theme.Brush("Pane");
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled" })
            box.Resources[key] = bg;
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = Theme.Brush("Text");
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver" })
            box.Resources[key] = Theme.Brush("Border");
        box.Resources["TextControlBorderBrushFocused"] = Theme.Brush("Accent");
        box.Resources["TextControlPlaceholderForeground"] = Theme.Brush("TextDim");
        box.Resources["TextControlPlaceholderForegroundFocused"] = Theme.Brush("TextDim");
        box.Resources["TextControlPlaceholderForegroundPointerOver"] = Theme.Brush("TextDim");
        box.Background = bg;
        box.Foreground = Theme.Brush("Text");
        box.BorderBrush = Theme.Brush("Border");
        box.PreviewKeyDown += OnKey;
        SetRow(box, 1);
        Children.Add(box);
    }

    public void FocusText()
    {
        box.Focus(FocusState.Programmatic);
        box.SelectionStart = box.Text.Length;
    }

    void Submit(bool enter)
    {
        if (box.Text.Length == 0) return;
        var text = box.Text;
        box.Text = "";
        Submitted?.Invoke(text, enter);
    }

    static bool Down(VirtualKey k) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; CloseRequested?.Invoke(); return; }
        if (e.Key != VirtualKey.Enter) return;
        if (Down(VirtualKey.Control)) { e.Handled = true; Submit(enter: true); }
        else if (Down(VirtualKey.Shift)) { e.Handled = true; Submit(enter: false); }
    }
}
