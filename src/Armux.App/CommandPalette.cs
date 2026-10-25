using Armux.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Armux.App;

/// <summary>
/// cmux command palette: a search box over every action, with each action's shortcut shown inline.
/// Up/Down or Ctrl+N/Ctrl+P move, Enter runs, Esc or a click outside closes.
/// </summary>
public sealed partial class CommandPalette : Grid
{
    readonly TextBox input;
    readonly StackPanel list = new();
    readonly ScrollViewer scroller;
    List<Command> all = [], shown = [];
    int index;

    public bool IsOpen => Visibility == Visibility.Visible;

    public CommandPalette()
    {
        Visibility = Visibility.Collapsed;
        Background = Theme.Brush("Overlay");
        Tapped += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) Close(); };

        input = new TextBox
        {
            PlaceholderText = "Type a command or workspace name",
            FontFamily = new FontFamily(Theme.UiFont),
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Background = Theme.Brush("Input"),
            Foreground = Theme.Brush("Text"),
            Padding = new Thickness(12, 8, 12, 8),
        };
        input.TextChanged += (_, _) => Filter();
        input.PreviewKeyDown += OnKey;

        scroller = new ScrollViewer { Content = list, MaxHeight = 380, Margin = new Thickness(0, 6, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(input);
        stack.Children.Add(scroller);

        var card = new Border
        {
            Child = stack,
            Width = 620,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(10),
            Background = Theme.Brush("Palette"),
            BorderBrush = Theme.Brush("Border"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 72, 0, 0),
            Shadow = new ThemeShadow(),
        };
        card.Translation = new System.Numerics.Vector3(0, 0, 32);
        Children.Add(card);
    }

    public void Open(List<Command> commands)
    {
        all = commands;
        input.Text = "";
        Filter();
        Visibility = Visibility.Visible;
        input.Focus(FocusState.Programmatic);
    }

    public void Close() => Visibility = Visibility.Collapsed;

    void Filter()
    {
        shown = CommandSearch.Filter(all, input.Text).Take(60).ToList();
        index = 0;
        Render();
    }

    void Render()
    {
        list.Children.Clear();
        for (int i = 0; i < shown.Count; i++)
        {
            var c = shown[i];
            bool sel = i == index;
            var row = new Grid { Padding = new Thickness(12, 7, 12, 7), CornerRadius = new CornerRadius(6), Background = sel ? Theme.Brush("PaletteSelected") : null };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Ui.Text(c.Title, 13.5, Theme.Brush(sel ? "SelectedText" : "Text")));
            var keys = Ui.Text(c.Shortcut, 12, Theme.Brush(sel ? "SelectedDim" : "TextFaint"));
            keys.Margin = new Thickness(16, 0, 0, 0);
            SetColumn(keys, 1);
            row.Children.Add(keys);
            int at = i;
            row.Tapped += (_, _) => { index = at; Run(); };
            list.Children.Add(row);
        }
        if (shown.Count == 0) list.Children.Add(new TextBlock { Text = "No matching commands", Margin = new Thickness(12, 8, 12, 8), Foreground = Theme.Brush("TextFaint") });
    }

    void Move(int delta)
    {
        if (shown.Count == 0) return;
        index = (index + delta + shown.Count) % shown.Count;
        Render();
        if (list.Children[index] is FrameworkElement el) el.StartBringIntoView();
    }

    void Run()
    {
        if (index < 0 || index >= shown.Count) return;
        var c = shown[index];
        Close();
        c.Run();
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Down: Move(1); break;
            case VirtualKey.Up: Move(-1); break;
            case VirtualKey.N when ctrl: Move(1); break;
            case VirtualKey.P when ctrl: Move(-1); break;
            case VirtualKey.Enter: Run(); break;
            case VirtualKey.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
