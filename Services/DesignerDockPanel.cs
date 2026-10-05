using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace XamlSub.Services;

/// <summary>
/// Small WinUI Panel used by the designer when the user selects DockPanel mode.
/// It performs real Measure/Arrange on its children; the editor overlay is not involved in layout.
/// </summary>
public sealed class DesignerDockPanel : Panel
{
    public static readonly DependencyProperty DockProperty = DependencyProperty.RegisterAttached(
        "Dock",
        typeof(string),
        typeof(DesignerDockPanel),
        new PropertyMetadata("Left", OnDockChanged));

    public static string GetDock(DependencyObject obj) => (string)obj.GetValue(DockProperty);
    public static void SetDock(DependencyObject obj, string value) => obj.SetValue(DockProperty, value);

    private static void OnDockChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && VisualTreeHelper.GetParent(element) is DesignerDockPanel panel)
            panel.InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = new Size();
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            desired.Width = Math.Max(desired.Width, child.DesiredSize.Width);
            desired.Height = Math.Max(desired.Height, child.DesiredSize.Height);
        }
        return new Size(
            double.IsInfinity(availableSize.Width) ? desired.Width : Math.Max(desired.Width, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? desired.Height : Math.Max(desired.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var remaining = new Rect(0, 0, finalSize.Width, finalSize.Height);

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            var dock = GetDock(child);
            var rect = remaining;

            switch (dock.ToLowerInvariant())
            {
                case "top":
                    rect.Height = Math.Min(desired.Height, Math.Max(0, remaining.Height));
                    remaining.Y += rect.Height;
                    remaining.Height = Math.Max(0, remaining.Height - rect.Height);
                    break;
                case "bottom":
                    rect.Y = Math.Max(remaining.Y, remaining.Bottom - Math.Min(desired.Height, remaining.Height));
                    rect.Height = Math.Min(desired.Height, remaining.Height);
                    remaining.Height = Math.Max(0, remaining.Height - rect.Height);
                    break;
                case "right":
                    rect.X = Math.Max(remaining.X, remaining.Right - Math.Min(desired.Width, remaining.Width));
                    rect.Width = Math.Min(desired.Width, remaining.Width);
                    remaining.Width = Math.Max(0, remaining.Width - rect.Width);
                    break;
                case "left":
                default:
                    rect.Width = Math.Min(desired.Width, Math.Max(0, remaining.Width));
                    remaining.X += rect.Width;
                    remaining.Width = Math.Max(0, remaining.Width - rect.Width);
                    break;
            }

            child.Arrange(rect);
        }

        // The last (undocked) child conventionally fills the remaining client area.
        // The simple designer behavior above intentionally keeps order/dock semantics visible.
        return finalSize;
    }
}
