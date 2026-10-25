using Armux.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Armux.App;

/// <summary>
/// Divider between the two halves of a split: a 1 px line with a 7 px hit area.
/// Dragging updates <see cref="SplitNode.Ratio"/> live; double-click resets to 50/50.
/// </summary>
public sealed partial class SplitterHandle : Grid
{
    public const double Size = 7;
    readonly SplitNode split;
    readonly Grid host;
    readonly Rectangle line = new();
    bool dragging;

    public SplitterHandle(SplitNode split, Grid host)
    {
        this.split = split;
        this.host = host;
        bool vertical = split.Direction == SplitDirection.Right;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        line.Fill = Theme.Brush("Divider");
        if (vertical) { line.Width = 1; line.HorizontalAlignment = HorizontalAlignment.Center; }
        else { line.Height = 1; line.VerticalAlignment = VerticalAlignment.Center; }
        Children.Add(line);
        // The hit area overlaps both neighbours by 3 px so the visible gap stays 1 px.
        Margin = vertical ? new Thickness(-3, 0, -3, 0) : new Thickness(0, -3, 0, -3);
        Canvas.SetZIndex(this, 10);

        ProtectedCursor = InputSystemCursor.Create(vertical ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
        PointerEntered += (_, _) => Highlight(true);
        PointerExited += (_, _) => { if (!dragging) Highlight(false); };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += (_, e) => { dragging = false; ReleasePointerCapture(e.Pointer); Highlight(false); };
        DoubleTapped += (_, _) => { split.Ratio = 0.5; Apply(); };
    }

    void Highlight(bool on)
    {
        line.Fill = Theme.Brush(on ? "Accent" : "Divider");
        if (split.Direction == SplitDirection.Right) line.Width = on ? 3 : 1; else line.Height = on ? 3 : 1;
    }

    void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        dragging = CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetCurrentPoint(host).Position;
        double ratio = split.Direction == SplitDirection.Right ? p.X / host.ActualWidth : p.Y / host.ActualHeight;
        split.Ratio = Math.Clamp(ratio, 0.1, 0.9);
        Apply();
    }

    public void Apply()
    {
        if (split.Direction == SplitDirection.Right)
        {
            host.ColumnDefinitions[0].Width = new GridLength(split.Ratio, GridUnitType.Star);
            host.ColumnDefinitions[2].Width = new GridLength(1 - split.Ratio, GridUnitType.Star);
        }
        else
        {
            host.RowDefinitions[0].Height = new GridLength(split.Ratio, GridUnitType.Star);
            host.RowDefinitions[2].Height = new GridLength(1 - split.Ratio, GridUnitType.Star);
        }
    }
}
