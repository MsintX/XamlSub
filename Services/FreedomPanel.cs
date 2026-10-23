using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace XamlSub.Services;

/// <summary>
/// Freely draggable, parent-relative layout panel used by the designer preview.
/// RelativeX/RelativeY describe a point on the parent surface, AnchorX/AnchorY select
/// which point on each child is attached to that coordinate, and optional RelativeWidth /
/// RelativeHeight values make a child's size responsive without changing free dragging.
/// All attached properties apply to any UIElement, not only buttons.
/// </summary>
public sealed class FreedomPanel : Panel
{
    public static readonly DependencyProperty RelativeXProperty = DependencyProperty.RegisterAttached(
        "RelativeX", typeof(double), typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativeLayoutChanged));

    public static readonly DependencyProperty RelativeYProperty = DependencyProperty.RegisterAttached(
        "RelativeY", typeof(double), typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativeLayoutChanged));

    public static readonly DependencyProperty AnchorXProperty = DependencyProperty.RegisterAttached(
        "AnchorX", typeof(double), typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativeLayoutChanged));

    public static readonly DependencyProperty AnchorYProperty = DependencyProperty.RegisterAttached(
        "AnchorY", typeof(double), typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativeLayoutChanged));

    /// <summary>Optional width as a fraction of the panel; zero keeps the control's natural/explicit width.</summary>
    public static readonly DependencyProperty RelativeWidthProperty = DependencyProperty.RegisterAttached(
        "RelativeWidth", typeof(double), typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativeLayoutChanged));

    /// <summary>Optional height as a fraction of the panel; zero keeps the control's natural/explicit height.</summary>
    public static readonly DependencyProperty RelativeHeightProperty = DependencyProperty.RegisterAttached(
        "RelativeHeight", typeof(double), typeof(FreedomPanel),
        new PropertyMetadata(0d, OnRelativeLayoutChanged));

    public static double GetRelativeX(DependencyObject obj) => (double)obj.GetValue(RelativeXProperty);
    public static void SetRelativeX(DependencyObject obj, double value) => obj.SetValue(RelativeXProperty, Clamp01(value));
    public static double GetRelativeY(DependencyObject obj) => (double)obj.GetValue(RelativeYProperty);
    public static void SetRelativeY(DependencyObject obj, double value) => obj.SetValue(RelativeYProperty, Clamp01(value));
    public static double GetAnchorX(DependencyObject obj) => (double)obj.GetValue(AnchorXProperty);
    public static void SetAnchorX(DependencyObject obj, double value) => obj.SetValue(AnchorXProperty, Clamp01(value));
    public static double GetAnchorY(DependencyObject obj) => (double)obj.GetValue(AnchorYProperty);
    public static void SetAnchorY(DependencyObject obj, double value) => obj.SetValue(AnchorYProperty, Clamp01(value));
    public static double GetRelativeWidth(DependencyObject obj) => (double)obj.GetValue(RelativeWidthProperty);
    public static void SetRelativeWidth(DependencyObject obj, double value) => obj.SetValue(RelativeWidthProperty, Clamp01(value));
    public static double GetRelativeHeight(DependencyObject obj) => (double)obj.GetValue(RelativeHeightProperty);
    public static void SetRelativeHeight(DependencyObject obj, double value) => obj.SetValue(RelativeHeightProperty, Clamp01(value));

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private static void OnRelativeLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && VisualTreeHelper.GetParent(element) is FreedomPanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            var relativeWidth = GetRelativeWidth(child);
            var relativeHeight = GetRelativeHeight(child);
            var widthConstraint = relativeWidth > 0 && double.IsFinite(availableSize.Width)
                ? availableSize.Width * relativeWidth
                : availableSize.Width;
            var heightConstraint = relativeHeight > 0 && double.IsFinite(availableSize.Height)
                ? availableSize.Height * relativeHeight
                : availableSize.Height;
            child.Measure(new Size(widthConstraint, heightConstraint));
        }

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
            var relativeWidth = GetRelativeWidth(child);
            var relativeHeight = GetRelativeHeight(child);
            var width = relativeWidth > 0 ? finalSize.Width * relativeWidth : desired.Width;
            var height = relativeHeight > 0 ? finalSize.Height * relativeHeight : desired.Height;
            width = Math.Clamp(width, 0, Math.Max(0, finalSize.Width));
            height = Math.Clamp(height, 0, Math.Max(0, finalSize.Height));

            var maxX = Math.Max(0, finalSize.Width - width);
            var maxY = Math.Max(0, finalSize.Height - height);
            var anchorX = Clamp01(GetAnchorX(child));
            var anchorY = Clamp01(GetAnchorY(child));
            var x = Clamp01(GetRelativeX(child)) * finalSize.Width - anchorX * width;
            var y = Clamp01(GetRelativeY(child)) * finalSize.Height - anchorY * height;
            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);

            child.Arrange(new Rect(x, y, width, height));
        }

        return finalSize;
    }
}
