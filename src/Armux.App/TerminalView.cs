using Armux.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Armux.App;

/// <summary>Phase 2 terminal panel: renders a surface's mock lines. Phase 3 replaces it with a ConPTY-backed renderer.</summary>
public sealed partial class TerminalView : Grid
{
    public TerminalView(Surface surface)
    {
        Background = Theme.Brush("Pane");
        var scroll = new ScrollViewer { Padding = new Thickness(12, 8, 12, 8), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(scroll);
        var text = new TextBlock
        {
            FontFamily = Theme.TerminalFont,
            FontSize = Theme.TerminalFontSize,
            LineHeight = Math.Round(Theme.TerminalFontSize * 1.4),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = Theme.Term(TermColor.Default),
        };
        for (int i = 0; i < surface.Lines.Count; i++)
        {
            if (i > 0) text.Inlines.Add(new LineBreak());
            foreach (var span in surface.Lines[i].Spans)
                text.Inlines.Add(new Run { Text = span.Text, Foreground = Theme.Term(span.Color) });
        }
        scroll.Content = text;
    }
}
