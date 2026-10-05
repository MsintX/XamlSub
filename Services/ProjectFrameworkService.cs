using System.Text;
using System.Xml.Linq;

namespace XamlSub.Services;

public enum XamlFrameworkKind
{
    Unknown,
    WinUI3,
    Uwp,
    Uno,
    Wpf,
    Avalonia,
    Maui
}

public sealed record ProjectFrameworkInfo(
    string ProjectPath,
    XamlFrameworkKind Framework,
    string? RootNamespace,
    string DisplayName)
{
    public bool IsWindowsXaml => Framework is XamlFrameworkKind.WinUI3 or XamlFrameworkKind.Uwp or XamlFrameworkKind.Uno;
    public bool SupportsFreedomPanel => Framework is XamlFrameworkKind.WinUI3 or XamlFrameworkKind.Uwp or XamlFrameworkKind.Uno or XamlFrameworkKind.Wpf or XamlFrameworkKind.Avalonia or XamlFrameworkKind.Maui;
}

public static class ProjectFrameworkService
{
    public static ProjectFrameworkInfo Detect(string projectPath)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("找不到项目文件。", projectPath);

        var framework = XamlFrameworkKind.Unknown;
        string? rootNamespace = null;

        try
        {
            var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
            rootNamespace = FindProperty(doc, "RootNamespace");
            var sdk = (string?)doc.Root?.Attribute("Sdk") ?? string.Empty;

            var properties = doc.Descendants()
                .Where(e => e.Parent?.Name.LocalName == "PropertyGroup")
                .GroupBy(e => e.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Last().Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);

            var packageIds = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e => ((string?)e.Attribute("Include") ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            if (IsTrue(properties, "UseMaui") || packageIds.Any(IsMauiPackage))
                framework = XamlFrameworkKind.Maui;
            else if (sdk.Contains("Uno.Sdk", StringComparison.OrdinalIgnoreCase) ||
                     packageIds.Any(id => id.StartsWith("Uno.", StringComparison.OrdinalIgnoreCase)) ||
                     packageIds.Any(id => id.Contains("Uno.UI", StringComparison.OrdinalIgnoreCase)))
                framework = XamlFrameworkKind.Uno;
            else if (IsTrue(properties, "UseWPF") ||
                     sdk.Contains("Microsoft.NET.Sdk.WindowsDesktop", StringComparison.OrdinalIgnoreCase))
                framework = XamlFrameworkKind.Wpf;
            else if (packageIds.Any(id => id.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)))
                framework = XamlFrameworkKind.Avalonia;
            else if (IsTrue(properties, "UseWinUI") ||
                     packageIds.Any(id => id.Equals("Microsoft.WindowsAppSDK", StringComparison.OrdinalIgnoreCase)))
                framework = XamlFrameworkKind.WinUI3;
            else if (sdk.Contains(".Uwp", StringComparison.OrdinalIgnoreCase) ||
                     packageIds.Any(id => id.Contains("Windows.UI.Xaml", StringComparison.OrdinalIgnoreCase)))
                framework = XamlFrameworkKind.Uwp;
        }
        catch
        {
            var source = File.ReadAllText(projectPath);
            framework = DetectFromText(source);
        }

        if (framework == XamlFrameworkKind.Unknown)
        {
            var source = File.ReadAllText(projectPath);
            framework = DetectFromText(source);
        }

        if (string.IsNullOrWhiteSpace(rootNamespace))
            rootNamespace = Path.GetFileNameWithoutExtension(projectPath);

        return new ProjectFrameworkInfo(
            projectPath,
            framework,
            rootNamespace,
            GetDisplayName(framework));
    }

    public static XamlFrameworkKind InferFromXaml(string xamlText)
    {
        if (xamlText.Contains("https://github.com/avaloniaui", StringComparison.OrdinalIgnoreCase))
            return XamlFrameworkKind.Avalonia;
        if (xamlText.Contains("http://schemas.microsoft.com/dotnet/2021/maui", StringComparison.OrdinalIgnoreCase) ||
            xamlText.Contains("Microsoft.Maui.Controls", StringComparison.OrdinalIgnoreCase) ||
            xamlText.Contains("xct:", StringComparison.OrdinalIgnoreCase))
            return XamlFrameworkKind.Maui;
        if (xamlText.Contains("using:Uno", StringComparison.OrdinalIgnoreCase))
            return XamlFrameworkKind.Uno;
        if (xamlText.Contains("Windows.UI.Xaml", StringComparison.OrdinalIgnoreCase))
            return XamlFrameworkKind.Uwp;
        if (xamlText.Contains("http://schemas.microsoft.com/winfx/2006/xaml/presentation", StringComparison.OrdinalIgnoreCase))
            return XamlFrameworkKind.WinUI3;
        return XamlFrameworkKind.Unknown;
    }

    public static string GetDisplayName(XamlFrameworkKind kind) => kind switch
    {
        XamlFrameworkKind.WinUI3 => "WinUI 3",
        XamlFrameworkKind.Uwp => "UWP",
        XamlFrameworkKind.Uno => "Uno Platform",
        XamlFrameworkKind.Wpf => "WPF",
        XamlFrameworkKind.Avalonia => "Avalonia",
        XamlFrameworkKind.Maui => ".NET MAUI",
        _ => "未知 XAML 框架"
    };

    public static string GetFreedomPanelNamespaceUri(XamlFrameworkKind framework, string clrNamespace)
        => framework switch
        {
            XamlFrameworkKind.WinUI3 or XamlFrameworkKind.Uno => $"using:{clrNamespace}",
            XamlFrameworkKind.Uwp => $"using:{clrNamespace}",
            XamlFrameworkKind.Wpf or XamlFrameworkKind.Maui => $"clr-namespace:{clrNamespace}",
            XamlFrameworkKind.Avalonia => $"using:{clrNamespace}",
            _ => $"using:{clrNamespace}"
        };

    public static string GetEventName(XamlFrameworkKind framework, string internalEventKind)
    {
        return framework switch
        {
            XamlFrameworkKind.Wpf when internalEventKind == "Tapped" => "MouseLeftButtonUp",
            XamlFrameworkKind.Avalonia when internalEventKind == "Tapped" => "PointerPressed",
            XamlFrameworkKind.Maui when internalEventKind == "Click" => "Clicked",
            XamlFrameworkKind.Maui => string.Empty,
            _ => internalEventKind
        };
    }

    public static string? GetEventArgsType(XamlFrameworkKind framework, string internalEventKind)
    {
        return framework switch
        {
            XamlFrameworkKind.WinUI3 or XamlFrameworkKind.Uno when internalEventKind == "Click" => "Microsoft.UI.Xaml.RoutedEventArgs",
            XamlFrameworkKind.WinUI3 or XamlFrameworkKind.Uno => "Microsoft.UI.Xaml.Input.TappedRoutedEventArgs",
            XamlFrameworkKind.Uwp when internalEventKind == "Click" => "Windows.UI.Xaml.RoutedEventArgs",
            XamlFrameworkKind.Uwp => "Windows.UI.Xaml.Input.TappedRoutedEventArgs",
            XamlFrameworkKind.Wpf when internalEventKind == "Click" => "System.Windows.RoutedEventArgs",
            XamlFrameworkKind.Wpf => "System.Windows.Input.MouseButtonEventArgs",
            XamlFrameworkKind.Avalonia when internalEventKind == "Tapped" => "Avalonia.Input.PointerPressedEventArgs",
            XamlFrameworkKind.Avalonia => "Avalonia.Interactivity.RoutedEventArgs",
            XamlFrameworkKind.Maui => "System.EventArgs",
            _ when internalEventKind == "Click" => "Microsoft.UI.Xaml.RoutedEventArgs",
            _ => "Microsoft.UI.Xaml.Input.TappedRoutedEventArgs"
        };
    }

    public static string GetLoadedEventArgsType(XamlFrameworkKind framework)
        => framework switch
        {
            XamlFrameworkKind.Uwp => "Windows.UI.Xaml.RoutedEventArgs",
            XamlFrameworkKind.Wpf => "System.Windows.RoutedEventArgs",
            XamlFrameworkKind.Avalonia => "Avalonia.Interactivity.RoutedEventArgs",
            XamlFrameworkKind.Maui => "System.EventArgs",
            _ => "Microsoft.UI.Xaml.RoutedEventArgs"
        };

    public static string GenerateFreedomPanelSource(ProjectFrameworkInfo info)
    {
        var ns = string.IsNullOrWhiteSpace(info.RootNamespace) ? "XamlSub.Generated" : info.RootNamespace!;
        return info.Framework switch
        {
            XamlFrameworkKind.Wpf => GenerateWpf(ns),
            XamlFrameworkKind.Avalonia => GenerateAvalonia(ns),
            XamlFrameworkKind.Maui => GenerateMaui(ns),
            XamlFrameworkKind.Uwp => GenerateUwp(ns),
            _ => GenerateWinUi(ns)
        };
    }

    public static string GenerateDefaultXaml(
        ProjectFrameworkInfo info,
        string xamlPath,
        string? classNamespace = null)
    {
        var ns = string.IsNullOrWhiteSpace(classNamespace) ? info.RootNamespace : classNamespace;
        ns ??= "XamlSub.Generated";
        var className = Path.GetFileNameWithoutExtension(xamlPath);
        if (string.IsNullOrWhiteSpace(className))
            className = info.Framework == XamlFrameworkKind.Maui ? "MainPage" : "MainWindow";

        var classIdentity = ns + "." + className;
        var lines = info.Framework switch
        {
            XamlFrameworkKind.Maui => new[]
            {
                "<ContentPage",
                "    xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"",
                "    xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"",
                "    x:Class=\"__CLASS_ID__\">",
                "    <Grid>",
                "    </Grid>",
                "</ContentPage>"
            },
            XamlFrameworkKind.Avalonia => new[]
            {
                "<Window",
                "    xmlns=\"https://github.com/avaloniaui\"",
                "    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
                "    x:Class=\"__CLASS_ID__\">",
                "    <Grid>",
                "    </Grid>",
                "</Window>"
            },
            XamlFrameworkKind.Wpf => new[]
            {
                "<Window",
                "    xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
                "    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
                "    x:Class=\"__CLASS_ID__\">",
                "    <Grid>",
                "    </Grid>",
                "</Window>"
            },
            XamlFrameworkKind.Uwp or XamlFrameworkKind.Uno => new[]
            {
                "<Page",
                "    xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
                "    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
                "    x:Class=\"__CLASS_ID__\">",
                "    <Grid>",
                "    </Grid>",
                "</Page>"
            },
            _ => new[]
            {
                "<Window",
                "    xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
                "    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
                "    x:Class=\"__CLASS_ID__\">",
                "    <Grid>",
                "    </Grid>",
                "</Window>"
            }
        };

        return string.Join(Environment.NewLine, lines)
            .Replace("__CLASS_ID__", classIdentity, StringComparison.Ordinal)
            + Environment.NewLine;
    }

    public static string GenerateCodeBehind(ProjectFrameworkInfo info, string xamlPath, string? classNamespace = null)
    {
        var ns = string.IsNullOrWhiteSpace(classNamespace) ? info.RootNamespace : classNamespace;
        ns ??= "XamlSub.Generated";
        var className = Path.GetFileNameWithoutExtension(xamlPath);
        if (string.IsNullOrWhiteSpace(className))
            className = info.Framework == XamlFrameworkKind.Maui ? "MainPage" : "MainWindow";

        var lines = info.Framework switch
        {
            XamlFrameworkKind.Wpf => new[]
            {
                "using System.Windows;", "", "namespace __NS__;", "",
                "public partial class __CLASS__ : Window", "{", "    public __CLASS__()", "    {",
                "        InitializeComponent();", "    }", "}"
            },
            XamlFrameworkKind.Avalonia => new[]
            {
                "using Avalonia.Controls;", "", "namespace __NS__;", "",
                "public partial class __CLASS__ : Window", "{", "    public __CLASS__()", "    {",
                "        InitializeComponent();", "    }", "}"
            },
            XamlFrameworkKind.Maui => new[]
            {
                "using Microsoft.Maui.Controls;", "", "namespace __NS__;", "",
                "public partial class __CLASS__ : ContentPage", "{", "    public __CLASS__()", "    {",
                "        InitializeComponent();", "    }", "}"
            },
            XamlFrameworkKind.Uwp => new[]
            {
                "using Windows.UI.Xaml.Controls;", "", "namespace __NS__;", "",
                "public sealed partial class __CLASS__ : Page", "{", "    public __CLASS__()", "    {",
                "        InitializeComponent();", "    }", "}"
            },
            XamlFrameworkKind.Uno => new[]
            {
                "using Microsoft.UI.Xaml.Controls;", "", "namespace __NS__;", "",
                "public partial class __CLASS__ : Page", "{", "    public __CLASS__()", "    {",
                "        InitializeComponent();", "    }", "}"
            },
            _ => new[]
            {
                "using Microsoft.UI.Xaml;", "", "namespace __NS__;", "",
                "public sealed partial class __CLASS__ : Window", "{", "    public __CLASS__()", "    {",
                "        InitializeComponent();", "    }", "}"
            }
        };

        return RenderTemplate(lines, ns, className);
    }

    public static async Task<bool> EnsureFreedomPanelSourceAsync(ProjectFrameworkInfo info)
    {
        if (!info.SupportsFreedomPanel || info.Framework == XamlFrameworkKind.Unknown)
            return false;

        var projectDir = Path.GetDirectoryName(info.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return false;

        try
        {
            var existing = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Any(p => File.ReadAllText(p).Contains("class FreedomPanel", StringComparison.Ordinal));
            if (existing)
                return false;

            var filePath = Path.Combine(projectDir, "FreedomPanel.cs");
            if (File.Exists(filePath))
                return false;

            await File.WriteAllTextAsync(filePath, GenerateFreedomPanelSource(info), new UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindProperty(XDocument doc, string name)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();

    private static bool IsTrue(IReadOnlyDictionary<string, string> props, string key)
        => props.TryGetValue(key, out var value) &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    private static bool IsMauiPackage(string id)
        => id.Equals("Microsoft.Maui.Controls", StringComparison.OrdinalIgnoreCase) ||
           id.StartsWith("Microsoft.Maui.", StringComparison.OrdinalIgnoreCase);

    private static XamlFrameworkKind DetectFromText(string source)
    {
        if (RegexLike(source, @"UseMaui\s*>\s*true|Microsoft\.Maui\.Controls"))
            return XamlFrameworkKind.Maui;
        if (RegexLike(source, @"Uno\.Sdk|Uno\.UI"))
            return XamlFrameworkKind.Uno;
        if (RegexLike(source, @"UseWPF\s*>\s*true|Microsoft\.NET\.Sdk\.WindowsDesktop"))
            return XamlFrameworkKind.Wpf;
        if (RegexLike(source, @"Avalonia"))
            return XamlFrameworkKind.Avalonia;
        if (RegexLike(source, @"Microsoft\.WindowsAppSDK|UseWinUI\s*>\s*true"))
            return XamlFrameworkKind.WinUI3;
        if (RegexLike(source, @"Windows\.UI\.Xaml"))
            return XamlFrameworkKind.Uwp;
        return XamlFrameworkKind.Unknown;
    }

    private static bool RegexLike(string source, string pattern)
        => System.Text.RegularExpressions.Regex.IsMatch(source, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string RenderTemplate(IEnumerable<string> lines, string ns, string className)
        => string.Join(Environment.NewLine, lines)
            .Replace("__NS__", ns, StringComparison.Ordinal)
            .Replace("__CLASS__", className, StringComparison.Ordinal)
            + Environment.NewLine;

    private static string GenerateWinUi(string ns) => RenderTemplate(new[]
    {
        "using System;",
        "using System.Linq;",
        "using Microsoft.UI.Xaml;",
        "using Microsoft.UI.Xaml.Controls;",
        "using Microsoft.UI.Xaml.Media;",
        "using Windows.Foundation;",
        "",
        "namespace __NS__;",
        "",
        "/// <summary>",
        "/// Responsive relative-position panel generated by XAML Sub.",
        "/// Children use FreedomPanel.RelativeX / RelativeY top-left coordinates in the 0..1 range.",
        "/// </summary>",
        "public sealed class FreedomPanel : Panel",
        "{",
        "    public static readonly DependencyProperty RelativeXProperty = DependencyProperty.RegisterAttached(",
        "        \"RelativeX\", typeof(double), typeof(FreedomPanel),",
        "        new PropertyMetadata(0d, OnRelativePositionChanged));",
        "",
        "    public static readonly DependencyProperty RelativeYProperty = DependencyProperty.RegisterAttached(",
        "        \"RelativeY\", typeof(double), typeof(FreedomPanel),",
        "        new PropertyMetadata(0d, OnRelativePositionChanged));",
        "",
        "    public static double GetRelativeX(DependencyObject obj) => (double)obj.GetValue(RelativeXProperty);",
        "    public static void SetRelativeX(DependencyObject obj, double value) => obj.SetValue(RelativeXProperty, Clamp01(value));",
        "    public static double GetRelativeY(DependencyObject obj) => (double)obj.GetValue(RelativeYProperty);",
        "    public static void SetRelativeY(DependencyObject obj, double value) => obj.SetValue(RelativeYProperty, Clamp01(value));",
        "",
        "    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));",
        "",
        "    private static void OnRelativePositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)",
        "    {",
        "        if (VisualTreeHelper.GetParent(d) is FreedomPanel panel)",
        "            panel.InvalidateArrange();",
        "    }",
        "",
        "    protected override Size MeasureOverride(Size availableSize)",
        "    {",
        "        foreach (var child in Children)",
        "            child.Measure(availableSize);",
        "",
        "        var width = double.IsInfinity(availableSize.Width) ? Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max() : availableSize.Width;",
        "        var height = double.IsInfinity(availableSize.Height) ? Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max() : availableSize.Height;",
        "        return new Size(Math.Max(0, width), Math.Max(0, height));",
        "    }",
        "",
        "    protected override Size ArrangeOverride(Size finalSize)",
        "    {",
        "        foreach (var child in Children)",
        "        {",
        "            var desired = child.DesiredSize;",
        "            var x = Math.Min(Clamp01(GetRelativeX(child)) * finalSize.Width, Math.Max(0, finalSize.Width - desired.Width));",
        "            var y = Math.Min(Clamp01(GetRelativeY(child)) * finalSize.Height, Math.Max(0, finalSize.Height - desired.Height));",
        "            child.Arrange(new Rect(x, y, desired.Width, desired.Height));",
        "        }",
        "",
        "        return finalSize;",
        "    }",
        "}"
    }, ns, "FreedomPanel");

    private static string GenerateUwp(string ns) => RenderTemplate(new[]
    {
        "using System;",
        "using System.Linq;",
        "using Windows.Foundation;",
        "using Windows.UI.Xaml;",
        "using Windows.UI.Xaml.Controls;",
        "",
        "namespace __NS__;",
        "",
        "public sealed class FreedomPanel : Panel",
        "{",
        "    public static readonly DependencyProperty RelativeXProperty = DependencyProperty.RegisterAttached(",
        "        \"RelativeX\", typeof(double), typeof(FreedomPanel), new PropertyMetadata(0d, OnRelativePositionChanged));",
        "    public static readonly DependencyProperty RelativeYProperty = DependencyProperty.RegisterAttached(",
        "        \"RelativeY\", typeof(double), typeof(FreedomPanel), new PropertyMetadata(0d, OnRelativePositionChanged));",
        "",
        "    public static double GetRelativeX(DependencyObject obj) => (double)obj.GetValue(RelativeXProperty);",
        "    public static void SetRelativeX(DependencyObject obj, double value) => obj.SetValue(RelativeXProperty, Clamp01(value));",
        "    public static double GetRelativeY(DependencyObject obj) => (double)obj.GetValue(RelativeYProperty);",
        "    public static void SetRelativeY(DependencyObject obj, double value) => obj.SetValue(RelativeYProperty, Clamp01(value));",
        "",
        "    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));",
        "",
        "    private static void OnRelativePositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)",
        "    {",
        "        (Window.Current?.Content as UIElement)?.InvalidateMeasure();",
        "    }",
        "",
        "    protected override Size MeasureOverride(Size availableSize)",
        "    {",
        "        foreach (UIElement child in Children)",
        "            child.Measure(availableSize);",
        "        return availableSize;",
        "    }",
        "",
        "    protected override Size ArrangeOverride(Size finalSize)",
        "    {",
        "        foreach (UIElement child in Children)",
        "        {",
        "            var desired = child.DesiredSize;",
        "            var x = Math.Min(Clamp01(GetRelativeX(child)) * finalSize.Width, Math.Max(0, finalSize.Width - desired.Width));",
        "            var y = Math.Min(Clamp01(GetRelativeY(child)) * finalSize.Height, Math.Max(0, finalSize.Height - desired.Height));",
        "            child.Arrange(new Rect(x, y, desired.Width, desired.Height));",
        "        }",
        "        return finalSize;",
        "    }",
        "}"
    }, ns, "FreedomPanel");

    private static string GenerateAvalonia(string ns) => RenderTemplate(new[]
    {
        "using System;",
        "using System.Linq;",
        "using Avalonia;",
        "using Avalonia.Controls;",
        "",
        "namespace __NS__;",
        "",
        "public sealed class FreedomPanel : Panel",
        "{",
        "    public static readonly AttachedProperty<double> RelativeXProperty = AvaloniaProperty.RegisterAttached<FreedomPanel, Control, double>(\"RelativeX\", defaultValue: 0d, inherits: false);",
        "    public static readonly AttachedProperty<double> RelativeYProperty = AvaloniaProperty.RegisterAttached<FreedomPanel, Control, double>(\"RelativeY\", defaultValue: 0d, inherits: false);",
        "",
        "    public static double GetRelativeX(Control obj) => obj.GetValue(RelativeXProperty);",
        "    public static void SetRelativeX(Control obj, double value) => obj.SetValue(RelativeXProperty, Clamp01(value));",
        "    public static double GetRelativeY(Control obj) => obj.GetValue(RelativeYProperty);",
        "    public static void SetRelativeY(Control obj, double value) => obj.SetValue(RelativeYProperty, Clamp01(value));",
        "",
        "    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));",
        "",
        "    protected override Size MeasureOverride(Size availableSize)",
        "    {",
        "        foreach (var child in Children)",
        "            child.Measure(availableSize);",
        "        var width = double.IsInfinity(availableSize.Width) ? Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max() : availableSize.Width;",
        "        var height = double.IsInfinity(availableSize.Height) ? Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max() : availableSize.Height;",
        "        return new Size(Math.Max(0, width), Math.Max(0, height));",
        "    }",
        "",
        "    protected override Size ArrangeOverride(Size finalSize)",
        "    {",
        "        foreach (var child in Children)",
        "        {",
        "            var desired = child.DesiredSize;",
        "            var x = Math.Min(Clamp01(GetRelativeX(child)) * finalSize.Width, Math.Max(0, finalSize.Width - desired.Width));",
        "            var y = Math.Min(Clamp01(GetRelativeY(child)) * finalSize.Height, Math.Max(0, finalSize.Height - desired.Height));",
        "            child.Arrange(new Rect(x, y, desired.Width, desired.Height));",
        "        }",
        "        return finalSize;",
        "    }",
        "}"
    }, ns, "FreedomPanel");

    private static string GenerateWpf(string ns) => RenderTemplate(new[]
    {
        "using System;",
        "using System.Linq;",
        "using System.Windows;",
        "using System.Windows.Controls;",
        "",
        "namespace __NS__;",
        "",
        "public sealed class FreedomPanel : Panel",
        "{",
        "    public static readonly DependencyProperty RelativeXProperty = DependencyProperty.RegisterAttached(\"RelativeX\", typeof(double), typeof(FreedomPanel), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));",
        "    public static readonly DependencyProperty RelativeYProperty = DependencyProperty.RegisterAttached(\"RelativeY\", typeof(double), typeof(FreedomPanel), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));",
        "",
        "    public static double GetRelativeX(DependencyObject obj) => (double)obj.GetValue(RelativeXProperty);",
        "    public static void SetRelativeX(DependencyObject obj, double value) => obj.SetValue(RelativeXProperty, Clamp01(value));",
        "    public static double GetRelativeY(DependencyObject obj) => (double)obj.GetValue(RelativeYProperty);",
        "    public static void SetRelativeY(DependencyObject obj, double value) => obj.SetValue(RelativeYProperty, Clamp01(value));",
        "",
        "    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));",
        "",
        "    protected override Size MeasureOverride(Size availableSize)",
        "    {",
        "        foreach (UIElement child in InternalChildren)",
        "            child.Measure(availableSize);",
        "        var width = double.IsInfinity(availableSize.Width) ? InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max() : availableSize.Width;",
        "        var height = double.IsInfinity(availableSize.Height) ? InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max() : availableSize.Height;",
        "        return new Size(Math.Max(0, width), Math.Max(0, height));",
        "    }",
        "",
        "    protected override Size ArrangeOverride(Size finalSize)",
        "    {",
        "        foreach (UIElement child in InternalChildren)",
        "        {",
        "            var desired = child.DesiredSize;",
        "            var x = Math.Min(Clamp01(GetRelativeX(child)) * finalSize.Width, Math.Max(0, finalSize.Width - desired.Width));",
        "            var y = Math.Min(Clamp01(GetRelativeY(child)) * finalSize.Height, Math.Max(0, finalSize.Height - desired.Height));",
        "            child.Arrange(new Rect(x, y, desired.Width, desired.Height));",
        "        }",
        "        return finalSize;",
        "    }",
        "}"
    }, ns, "FreedomPanel");

    private static string GenerateMaui(string ns) => RenderTemplate(new[]
    {
        "using System;",
        "using System.Linq;",
        "using Microsoft.Maui;",
        "using Microsoft.Maui.Controls;",
        "",
        "namespace __NS__;",
        "",
        "public sealed class FreedomPanel : Layout",
        "{",
        "    public static readonly BindableProperty RelativeXProperty = BindableProperty.CreateAttached(\"RelativeX\", typeof(double), typeof(FreedomPanel), 0d);",
        "    public static readonly BindableProperty RelativeYProperty = BindableProperty.CreateAttached(\"RelativeY\", typeof(double), typeof(FreedomPanel), 0d);",
        "",
        "    public static double GetRelativeX(BindableObject obj) => (double)obj.GetValue(RelativeXProperty);",
        "    public static void SetRelativeX(BindableObject obj, double value) => obj.SetValue(RelativeXProperty, Clamp01(value));",
        "    public static double GetRelativeY(BindableObject obj) => (double)obj.GetValue(RelativeYProperty);",
        "    public static void SetRelativeY(BindableObject obj, double value) => obj.SetValue(RelativeYProperty, Clamp01(value));",
        "",
        "    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));",
        "",
        "    protected override ILayoutManager CreateLayoutManager() => new FreedomLayoutManager(this);",
        "",
        "    private sealed class FreedomLayoutManager : ILayoutManager",
        "    {",
        "        private readonly FreedomPanel _panel;",
        "        public FreedomLayoutManager(FreedomPanel panel) => _panel = panel;",
        "",
        "        public Size Measure(double widthConstraint, double heightConstraint)",
        "        {",
        "            foreach (var child in _panel.Children)",
        "                child.Measure(widthConstraint, heightConstraint);",
        "            var width = double.IsInfinity(widthConstraint) ? _panel.Children.Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max() : widthConstraint;",
        "            var height = double.IsInfinity(heightConstraint) ? _panel.Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max() : heightConstraint;",
        "            return new Size(Math.Max(0, width), Math.Max(0, height));",
        "        }",
        "",
        "        public Size ArrangeChildren(Rect bounds)",
        "        {",
        "            foreach (var child in _panel.Children)",
        "            {",
        "                var desired = child.DesiredSize;",
        "                var x = bounds.X + Math.Min(Clamp01(GetRelativeX(child)) * bounds.Width, Math.Max(0, bounds.Width - desired.Width));",
        "                var y = bounds.Y + Math.Min(Clamp01(GetRelativeY(child)) * bounds.Height, Math.Max(0, bounds.Height - desired.Height));",
        "                child.Arrange(new Rect(x, y, desired.Width, desired.Height));",
        "            }",
        "            return bounds.Size;",
        "        }",
        "    }",
        "}"
    }, ns, "FreedomPanel");
}
