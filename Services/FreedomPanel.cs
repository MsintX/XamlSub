using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace XamlSub.Services;

/// <summary>
/// Responsive relative-positioning panel used by the designer preview.
/// Each child stores its top-left RelativeX / RelativeY in the 0..1 range. The panel itself
/// stretches to its parent, so resizing the window scales the stored position with it.
/// </summary>
public sealed class FreedomPanel : Panel
{
    public static readonly DependencyProperty RelativeXProperty = DependencyProperty.RegisterAttached(
        "RelativeX",
        typeof(double),
        typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativePositionChanged));

    public static readonly DependencyProperty RelativeYProperty = DependencyProperty.RegisterAttached(
        "RelativeY",
        typeof(double),
        typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativePositionChanged));

    public static double GetRelativeX(DependencyObject obj) => (double)obj.GetValue(RelativeXProperty);

    public static void SetRelativeX(DependencyObject obj, double value)
        => obj.SetValue(RelativeXProperty, Math.Clamp(value, 0d, 1d));

    public static double GetRelativeY(DependencyObject obj) => (double)obj.GetValue(RelativeYProperty);

    public static void SetRelativeY(DependencyObject obj, double value)
        => obj.SetValue(RelativeYProperty, Math.Clamp(value, 0d, 1d));

    private static void OnRelativePositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element &&
            VisualTreeHelper.GetParent(element) is FreedomPanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(availableSize);

        var width = double.IsInfinity(availableSize.Width)
            ? Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max()
            : availableSize.Width;

        var height = double.IsInfinity(availableSize.Height)
            ? Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max()
            : availableSize.Height;

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            // RelativeX / RelativeY describe the control origin as a fraction of the
            // entire parent surface. Clamp the resulting origin so the control itself
            // remains inside the panel. This makes drag coordinates line up exactly
            // with the visual bounds used by the designer overlay.
            var maxX = Math.Max(0, finalSize.Width - desired.Width);
            var maxY = Math.Max(0, finalSize.Height - desired.Height);
            var x = Math.Clamp(GetRelativeX(child), 0d, 1d) * finalSize.Width;
            var y = Math.Clamp(GetRelativeY(child), 0d, 1d) * finalSize.Height;
            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
        }

        return finalSize;
    }
}
