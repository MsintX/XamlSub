using System.Xml;
using System.Xml.Linq;
using XamlSub.Models;

namespace XamlSub.Services;

/// <summary>
/// Narrow XAML reader/writer for the designer surface. Unsupported structures are preserved
/// and ignored by the visual model instead of being rewritten or discarded.
/// </summary>
public sealed class XamlDocumentService
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly HashSet<string> KnownEventAttributes = new(StringComparer.Ordinal)
    {
        "Click", "Tapped", "Loaded", "MouseLeftButtonUp", "PointerPressed", "Clicked",
        "RelativeX", "RelativeY"
    };

    public static readonly HashSet<string> DesignableControlTypes = new(StringComparer.Ordinal)
    {
        "Button", "TextBlock", "TextBox", "PasswordBox", "AutoSuggestBox", "NumberBox", "RichEditBox",
        "Image", "Icon", "FontIcon", "CheckBox", "RadioButton", "ToggleSwitch", "ToggleButton", "RepeatButton",
        "HyperlinkButton", "Slider", "ProgressBar", "ProgressRing", "ListView", "ListBox", "TreeView",
        "ItemsRepeater", "ComboBox", "DatePicker", "CalendarDatePicker", "TimePicker", "CalendarView",
        "Expander", "InfoBar", "RatingControl", "NavigationView", "Pivot", "TabView", "ScrollViewer", "CommandBar"
    };

    private static readonly HashSet<string> EditablePropertyNames = new(StringComparer.Ordinal)
    {
        "ToolTip", "PlaceholderText", "Header", "Text", "Content", "Source", "Stretch",
        "MinWidth", "MaxWidth", "MinHeight", "MaxHeight", "Margin", "Padding",
        "HorizontalContentAlignment", "VerticalContentAlignment", "FontSize", "FontWeight",
        "CharacterSpacing", "Opacity", "IsEnabled", "Visibility", "IsHitTestVisible", "IsTabStop",
        "Foreground", "Background", "BorderBrush", "BorderThickness", "CornerRadius",
        "TextWrapping", "AcceptsReturn", "IsReadOnly", "MaxLength", "DisplayMemberPath",
        "SelectedIndex", "IsEditable", "IsChecked", "ThreeState", "GroupName", "IsOn",
        "OnContent", "OffContent", "Minimum", "Maximum", "Value", "Orientation", "IsIndeterminate",
        "Date", "SelectedDate", "DateFormat", "SelectedTime", "NumberFormat", "PlaceholderValue",
        "IsDefault", "IsCancel", "ClickMode", "Command", "CommandParameter", "ItemsSource",
        "FontFamily", "FontStyle", "TextAlignment", "FlowDirection", "TabIndex", "UseSystemFocusVisuals",
        "DockPanel.Dock", "Canvas.Left", "Canvas.Top",
        "IsIndeterminate", "DateFormat", "NumberFormat", "Language", "Tag"
    };

    private static readonly HashSet<string> RootEditableProperties = new(StringComparer.Ordinal)
    {
        "Title", "Width", "Height", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight",
        "RequestedTheme", "ExtendsContentIntoTitleBar"
    };

    public const string DefaultGridXaml = "<Grid\n    xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n    <Grid.RowDefinitions>\n        <RowDefinition Height=\"*\"/>\n    </Grid.RowDefinitions>\n    <Grid.ColumnDefinitions>\n        <ColumnDefinition Width=\"*\"/>\n    </Grid.ColumnDefinitions>\n</Grid>";

    public static bool IsBlankFile(string path) => File.Exists(path) && string.IsNullOrWhiteSpace(File.ReadAllText(path));

    public GridDocument Load(string path, XamlFrameworkKind framework = XamlFrameworkKind.Unknown)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到指定的 XAML 文件。", path);
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"XAML 文件为空，无法打开：\n{path}");

        XDocument doc;
        try { doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo); }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"XAML XML 解析失败：{ex.Message}\n位置：第 {ex.LineNumber} 行，第 {ex.LinePosition} 列。", ex);
        }

        var documentRoot = doc.Root ?? throw new InvalidOperationException($"XAML 没有根元素：\n{path}");
        var grid = FindEditableGrid(documentRoot);
        var model = new GridDocument
        {
            FilePath = path,
            OriginalXamlText = text,
            RootTypeName = documentRoot.Name.LocalName,
            Framework = framework == XamlFrameworkKind.Unknown
                ? ProjectFrameworkService.InferFromXaml(text)
                : framework,
            FreedomPanelNamespaceUri = grid.Name.LocalName == "FreedomPanel" ? grid.Name.NamespaceName : null,
            RelativeFreePositioning = grid.Name.LocalName == "FreedomPanel",
            XClass = (string?)documentRoot.Attribute(X + "Class"),
            RootXName = (string?)grid.Attribute(X + "Name")
        };

        foreach (var attr in documentRoot.Attributes())
        {
            if (attr.IsNamespaceDeclaration || attr.Name == X + "Class") continue;
            if (RootEditableProperties.Contains(attr.Name.LocalName))
                model.RootProperties[attr.Name.LocalName] = attr.Value;
            else
                model.RootExtraAttributes[attr.Name.ToString()] = attr.Value;
        }

        var rowDefinitions = FindDefinitionsElement(grid, true);
        var columnDefinitions = FindDefinitionsElement(grid, false);
        var rowAttribute = (string?)grid.Attribute("RowDefinitions");
        var columnAttribute = (string?)grid.Attribute("ColumnDefinitions");
        bool rowSizingSupported;
        bool columnSizingSupported;
        model.Rows = rowDefinitions is not null
            ? ParseDefinitions(rowDefinitions, "RowDefinition", "Height", out rowSizingSupported)
            : ParseDefinitionAttribute(rowAttribute, out rowSizingSupported);
        model.Columns = columnDefinitions is not null
            ? ParseDefinitions(columnDefinitions, "ColumnDefinition", "Width", out columnSizingSupported)
            : ParseDefinitionAttribute(columnAttribute, out columnSizingSupported);
        model.GridSizingSupported = rowSizingSupported && columnSizingSupported;

        var sourceOrdinal = 0;
        foreach (var element in grid.Elements())
        {
            if (IsRowDefinitionsElement(element) || IsColumnDefinitionsElement(element)) continue;
            var actualIndex = sourceOrdinal++;

            if (!IsDesignableControl(element.Name.LocalName))
            {
                model.IgnoredElementCount++;
                continue;
            }

            // Container/template structures stay preserved, but a simple direct control is editable.
            if (element.HasElements)
            {
                model.IgnoredElementCount++;
                continue;
            }

            var node = new ControlNode
            {
                SourceKey = actualIndex,
                NamespaceUri = element.Name.NamespaceName,
                TypeName = element.Name.LocalName,
                XName = (string?)element.Attribute(X + "Name"),
                Content = (string?)element.Attribute("Content") ?? (string?)element.Attribute("Text"),
                Row = IntAttr(element, "Grid.Row"),
                Column = IntAttr(element, "Grid.Column"),
                RowSpan = Math.Max(1, IntAttr(element, "Grid.RowSpan", 1)),
                ColumnSpan = Math.Max(1, IntAttr(element, "Grid.ColumnSpan", 1)),
                Width = (string?)element.Attribute("Width"),
                Height = (string?)element.Attribute("Height"),
                HorizontalAlignment = (string?)element.Attribute("HorizontalAlignment"),
                VerticalAlignment = (string?)element.Attribute("VerticalAlignment"),
                EventName = FindInternalEventValue(element, framework),
                OffsetX = ParseDesignerOffset(element, "Canvas.Left", (string?)element.Attribute("Margin"), 0),
                OffsetY = ParseDesignerOffset(element, "Canvas.Top", (string?)element.Attribute("Margin"), 1),
                FreedomRelativeX = grid.Name.LocalName == "FreedomPanel" ? ParseFreedomRelative(element, "RelativeX") : null,
                FreedomRelativeY = grid.Name.LocalName == "FreedomPanel" ? ParseFreedomRelative(element, "RelativeY") : null,
                Dock = (string?)element.Attribute("DockPanel.Dock"),
                DesignerPositionCustomized = false
            };
            node.EventKind = FindInternalEventKind(element, framework);
            node.EventWasAutoGenerated = node.EventName is not null && !string.IsNullOrWhiteSpace(node.XName) &&
                string.Equals(node.EventName, $"{node.XName}_{node.EventKind}", StringComparison.Ordinal);

            foreach (var attr in element.Attributes())
            {
                if (attr.IsNamespaceDeclaration || attr.Name == X + "Name" ||
                    attr.Name.LocalName == "Content" || attr.Name.LocalName == "Text" ||
                    attr.Name.LocalName == "Width" || attr.Name.LocalName == "Height" ||
                    attr.Name.LocalName == "HorizontalAlignment" || attr.Name.LocalName == "VerticalAlignment" ||
                    attr.Name.LocalName == "Grid.Row" || attr.Name.LocalName == "Grid.Column" ||
                    attr.Name.LocalName == "Grid.RowSpan" || attr.Name.LocalName == "Grid.ColumnSpan" ||
                    attr.Name.LocalName == "DockPanel.Dock" ||
                    KnownEventAttributes.Contains(attr.Name.LocalName))
                    continue;
                if (EditablePropertyNames.Contains(attr.Name.LocalName)) node.Properties[attr.Name.LocalName] = attr.Value;
                else node.ExtraAttributes[attr.Name.ToString()] = attr.Value;
            }
            model.Nodes.Add(node);
        }

        model.Normalize();
        NormalizeFreedomCoordinates(model);
        return model;
    }

    public string Serialize(GridDocument model)
    {
        model.Normalize();
        var doc = !string.IsNullOrWhiteSpace(model.OriginalXamlText)
            ? XDocument.Parse(model.OriginalXamlText!, LoadOptions.PreserveWhitespace)
            : CreateBlankDocument(model);
        var documentRoot = doc.Root ?? throw new InvalidOperationException("XAML 没有根元素。");
        var root = FindEditableGrid(documentRoot);

        if (model.RelativeFreePositioning)
        {
            root = EnsureFreedomPanelRoot(documentRoot, root, model);
        }
        else if (root.Name.LocalName == "FreedomPanel")
        {
            root = ConvertFreedomPanelToGrid(root);
            model.FreedomPanelNamespaceUri = null;
        }

        SetOrRemove(documentRoot, X + "Class", model.XClass);
        foreach (var property in RootEditableProperties)
            SetOrRemove(documentRoot, property, model.RootProperties.TryGetValue(property, out var value) ? value : null);

        var protectedRootNames = new HashSet<string>(RootEditableProperties, StringComparer.Ordinal)
        {
            (X + "Class").ToString()
        };
        foreach (var pair in model.RootExtraAttributes)
        {
            if (!protectedRootNames.Contains(pair.Key))
                documentRoot.SetAttributeValue(ParseXName(pair.Key), pair.Value);
        }

        SetOrRemove(root, X + "Name", model.RootXName);

        if (model.GridSizingSupported && root.Name.LocalName == "Grid")
        {
            if (root.Attribute("RowDefinitions") is not null || root.Attribute("ColumnDefinitions") is not null)
            {
                root.SetAttributeValue("RowDefinitions", string.Join(",", Enumerable.Repeat("*", model.Rows)));
                root.SetAttributeValue("ColumnDefinitions", string.Join(",", Enumerable.Repeat("*", model.Columns)));
            }
            else
            {
                UpdateDefinitions(root, "Grid.RowDefinitions", "RowDefinition", "Height", model.Rows);
                UpdateDefinitions(root, "Grid.ColumnDefinitions", "ColumnDefinition", "Width", model.Columns);
            }
        }

        var sourceElements = root.Elements().Where(e => !IsRowDefinitionsElement(e) && !IsColumnDefinitionsElement(e)).ToList();
        var sourceDesignElements = sourceElements.Select((element, index) => (element, index))
            .Where(x => IsDesignableControl(x.element.Name.LocalName) && !x.element.HasElements)
            .ToDictionary(x => x.index, x => x.element);
        var existingKeys = model.Nodes.Where(n => n.SourceKey.HasValue).Select(n => n.SourceKey!.Value).ToHashSet();
        foreach (var old in sourceDesignElements.Where(x => !existingKeys.Contains(x.Key)).OrderByDescending(x => x.Key)) old.Value.Remove();

        foreach (var node in model.Nodes)
        {
            XElement element;
            if (node.SourceKey is int key && sourceDesignElements.TryGetValue(key, out var original))
            {
                element = original;
                element.Name = XNamespace.Get(node.NamespaceUri ?? Ui.NamespaceName) + node.TypeName;
            }
            else
            {
                element = sourceElements.FirstOrDefault(e => IsDesignableControl(e.Name.LocalName) &&
                    string.Equals((string?)e.Attribute(X + "Name"), node.XName, StringComparison.Ordinal))
                    ?? new XElement(XNamespace.Get(node.NamespaceUri ?? Ui.NamespaceName) + node.TypeName);
                if (element.Parent is null) root.Add(PrettyNewlineFor(root), element);
            }
            ApplyNodeAttributes(element, node, model);
        }

        return doc.ToString(SaveOptions.None);
    }

    private static XElement EnsureFreedomPanelRoot(XElement documentRoot, XElement currentRoot, GridDocument model)
    {
        var clrNamespace = GetFreedomPanelClrNamespace(model);
        var xmlns = ProjectFrameworkService.GetFreedomPanelNamespaceUri(model.Framework, clrNamespace);
        var panelXNamespace = XNamespace.Get(xmlns);

        var existing = currentRoot;
        if (!string.Equals(existing.Name.LocalName, "FreedomPanel", StringComparison.Ordinal))
        {
            existing.Name = panelXNamespace + "FreedomPanel";

            foreach (var child in existing.Elements().Where(e =>
                         e.Name.LocalName is "Grid.RowDefinitions" or "Grid.ColumnDefinitions" or
                         "RowDefinitions" or "ColumnDefinitions").ToList())
                child.Remove();

            existing.Attribute("RowDefinitions")?.Remove();
            existing.Attribute("ColumnDefinitions")?.Remove();
        }

        model.FreedomPanelNamespaceUri = xmlns;

        var prefix = FindOrCreatePrefix(documentRoot, panelXNamespace, "freedom");
        existing.Name = panelXNamespace + "FreedomPanel";
        SetNamespaceDeclaration(existing, prefix, xmlns);

        foreach (var element in existing.Elements().ToList())
        {
            if (!IsDesignableControl(element.Name.LocalName) || element.HasElements)
                continue;

            var x = ParseFreedomRelative(element, "RelativeX");
            var y = ParseFreedomRelative(element, "RelativeY");
            if (x is null)
            {
                var oldX = ParseDesignerOffset(element, "Canvas.Left", (string?)element.Attribute("Margin"), 0);
                x = NormalizeFreedomValue(oldX, 900);
            }
            if (y is null)
            {
                var oldY = ParseDesignerOffset(element, "Canvas.Top", (string?)element.Attribute("Margin"), 1);
                y = NormalizeFreedomValue(oldY, 678);
            }

            SetAttachedFreedomValue(element, panelXNamespace, prefix, "RelativeX", x.Value);
            SetAttachedFreedomValue(element, panelXNamespace, prefix, "RelativeY", y.Value);

            element.Attribute("Canvas.Left")?.Remove();
            element.Attribute("Canvas.Top")?.Remove();
            element.Attribute("Grid.Row")?.Remove();
            element.Attribute("Grid.Column")?.Remove();
            element.Attribute("Grid.RowSpan")?.Remove();
            element.Attribute("Grid.ColumnSpan")?.Remove();
        }

        return existing;
    }

    private static XElement ConvertFreedomPanelToGrid(XElement panel)
    {
        // The model deliberately keeps each node's last Grid row/column while it
        // is being displayed by FreedomPanel. This conversion only changes the
        // root back to Grid and removes FreedomPanel attached properties;
        // Serialize() will immediately write the preserved Grid.Row / Grid.Column
        // values through ApplyNodeAttributes().
        var gridNs = panel.Parent?.Name.Namespace ?? XNamespace.Get(Ui.NamespaceName);
        panel.Name = gridNs + "Grid";

        foreach (var child in panel.Elements())
        {
            foreach (var attr in child.Attributes().ToList())
            {
                if (attr.Name.LocalName is "RelativeX" or "RelativeY")
                    attr.Remove();
            }
        }

        return panel;
    }

    private static string GetFreedomPanelClrNamespace(GridDocument model)
    {
        if (!string.IsNullOrWhiteSpace(model.FreedomPanelClrNamespace))
            return model.FreedomPanelClrNamespace!;

        if (!string.IsNullOrWhiteSpace(model.XClass))
        {
            var normalized = model.XClass!.Replace("global::", "", StringComparison.Ordinal);
            var lastDot = normalized.LastIndexOf('.');
            if (lastDot > 0)
                return normalized[..lastDot];
        }

        if (!string.IsNullOrWhiteSpace(model.FreedomPanelNamespaceUri))
        {
            var uri = model.FreedomPanelNamespaceUri!;
            if (uri.StartsWith("using:", StringComparison.Ordinal))
                return uri["using:".Length..];
            if (uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
                return uri["clr-namespace:".Length..].Split(';')[0];
        }

        return "XamlSub.Generated";
    }

    private static string FindOrCreatePrefix(XElement root, XNamespace ns, string preferred)
    {
        var existing = root.GetNamespaceOfPrefix(preferred);
        if (existing == ns)
            return preferred;

        if (existing is null)
            return preferred;

        var index = 2;
        while (root.GetNamespaceOfPrefix(preferred + index) is not null)
            index++;
        return preferred + index;
    }

    private static void SetNamespaceDeclaration(XElement root, string prefix, string namespaceUri)
        => root.SetAttributeValue(XNamespace.Xmlns + prefix, namespaceUri);

    private static void SetAttachedFreedomValue(XElement element, XNamespace ns, string prefix, string name, double value)
        => element.SetAttributeValue(ns + name, value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

    private static double? ParseFreedomRelative(XElement element, string name)
    {
        var attr = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (attr is null) return null;
        return double.TryParse(attr.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0d, 1d)
            : null;
    }

    private static double NormalizeFreedomValue(double value, double baseSize)
        => Math.Clamp(value / Math.Max(1d, baseSize), 0d, 1d);

    private static void NormalizeFreedomCoordinates(GridDocument model)
    {
        if (model.FreedomPanelNamespaceUri is null && !string.Equals(model.RootTypeName, "FreedomPanel", StringComparison.Ordinal))
            return;

        foreach (var node in model.Nodes)
        {
            if (node.FreedomRelativeX is not null)
                node.OffsetX = node.FreedomRelativeX.Value * 900d;
            else
                node.FreedomRelativeX = NormalizeFreedomValue(node.OffsetX, 900d);

            if (node.FreedomRelativeY is not null)
                node.OffsetY = node.FreedomRelativeY.Value * 678d;
            else
                node.FreedomRelativeY = NormalizeFreedomValue(node.OffsetY, 678d);
        }
    }

    private static string? FindInternalEventValue(XElement element, XamlFrameworkKind framework)
    {
        foreach (var internalKind in new[] { "Click", "Tapped" })
        {
            var externalName = ProjectFrameworkService.GetEventName(framework, internalKind);
            if (!string.IsNullOrWhiteSpace(externalName))
            {
                var value = (string?)element.Attribute(externalName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return (string?)element.Attribute("Click") ?? (string?)element.Attribute("Tapped");
    }

    private static string? FindInternalEventKind(XElement element, XamlFrameworkKind framework)
    {
        foreach (var internalKind in new[] { "Click", "Tapped" })
        {
            var externalName = ProjectFrameworkService.GetEventName(framework, internalKind);
            if (!string.IsNullOrWhiteSpace(externalName) && element.Attribute(externalName) is not null)
                return internalKind;
        }

        return element.Attribute("Click") is not null ? "Click"
            : element.Attribute("Tapped") is not null ? "Tapped"
            : null;
    }

    private static void ApplyNodeAttributes(XElement element, ControlNode node, GridDocument model)
    {
        SetOrRemove(element, X + "Name", node.XName);
        var usesText = node.TypeName == "TextBlock" || node.TypeName == "TextBox" || node.TypeName == "PasswordBox" || node.TypeName == "RichEditBox" || node.TypeName == "AutoSuggestBox";
        var usesContent = node.TypeName == "Button" || node.TypeName == "HyperlinkButton" || node.TypeName == "ToggleButton" || node.TypeName == "RepeatButton" || node.TypeName == "CheckBox" || node.TypeName == "RadioButton" || node.TypeName == "Expander";
        SetOrRemove(element, "Content", usesContent ? node.Content : null);
        SetOrRemove(element, "Text", usesText ? node.Content : null);
        var freeRelative = model.RelativeFreePositioning;
        var rowText = freeRelative ? null : node.Row.ToString();
        var columnText = freeRelative ? null : node.Column.ToString();
        string? rowSpanText = freeRelative ? null : (node.RowSpan > 1 ? node.RowSpan.ToString() : null);
        string? columnSpanText = freeRelative ? null : (node.ColumnSpan > 1 ? node.ColumnSpan.ToString() : null);

        SetOrRemove(element, "Grid.Row", rowText);
        SetOrRemove(element, "Grid.Column", columnText);
        SetOrRemove(element, "Grid.RowSpan", rowSpanText);
        SetOrRemove(element, "Grid.ColumnSpan", columnSpanText);
        SetOrRemove(element, "Width", node.Width);
        SetOrRemove(element, "Height", node.Height);
        SetOrRemove(element, "HorizontalAlignment", freeRelative ? "Left" : node.HorizontalAlignment);
        SetOrRemove(element, "VerticalAlignment", freeRelative ? "Top" : node.VerticalAlignment);
        if (!freeRelative && node.DesignerPositionCustomized)
            SetOrRemove(element, "Margin", FormatMargin(node.OffsetX, node.OffsetY));
        if (!string.IsNullOrWhiteSpace(node.Dock)) SetOrRemove(element, "DockPanel.Dock", node.Dock);
        else element.Attribute("DockPanel.Dock")?.Remove();

        foreach (var name in new[] { "Click", "Tapped", "MouseLeftButtonUp", "PointerPressed", "Clicked" })
            element.Attribute(name)?.Remove();
        if (!string.IsNullOrWhiteSpace(node.EventName))
        {
            var eventKind = node.EventKind ?? node.DerivedEventKind;
            if (eventKind is "Click" or "Tapped")
            {
                var frameworkEventName = ProjectFrameworkService.GetEventName(model.Framework, eventKind);
                if (!string.IsNullOrWhiteSpace(frameworkEventName))
                    element.SetAttributeValue(frameworkEventName, node.EventName);
            }
        }

        foreach (var property in EditablePropertyNames)
        {
            if (property == "Content" || property == "Text") continue;
            if (freeRelative &&
                (property == "Margin" || property == "HorizontalAlignment" || property == "VerticalAlignment"))
            {
                continue;
            }
            if (node.Properties.TryGetValue(property, out var value))
                SetOrRemove(element, property, value);
        }

        if (freeRelative && !string.IsNullOrWhiteSpace(model.FreedomPanelNamespaceUri))
        {
            var freedomNs = XNamespace.Get(model.FreedomPanelNamespaceUri!);
            var prefix = FindOrCreatePrefix(element.Document?.Root ?? element, freedomNs, "freedom");
            SetNamespaceDeclaration(element.Document?.Root ?? element, prefix, model.FreedomPanelNamespaceUri!);

            var relativeX = node.FreedomRelativeX ?? NormalizeFreedomValue(node.OffsetX, 900d);
            var relativeY = node.FreedomRelativeY ?? NormalizeFreedomValue(node.OffsetY, 678d);
            SetAttachedFreedomValue(element, freedomNs, prefix, "RelativeX", relativeX);
            SetAttachedFreedomValue(element, freedomNs, prefix, "RelativeY", relativeY);

            SetOrRemove(element, "HorizontalAlignment", "Left");
            SetOrRemove(element, "VerticalAlignment", "Top");
            SetOrRemove(element, "Margin", null);
            SetOrRemove(element, "Canvas.Left", null);
            SetOrRemove(element, "Canvas.Top", null);
            SetOrRemove(element, "DockPanel.Dock", null);
        }
        else
        {
            foreach (var attr in element.Attributes().Where(a => a.Name.LocalName is "RelativeX" or "RelativeY").ToList())
                attr.Remove();
        }

        var protectedNames = new HashSet<string>(EditablePropertyNames, StringComparer.Ordinal);
        protectedNames.Add((X + "Name").ToString());
        protectedNames.Add("Grid.Row");
        protectedNames.Add("Grid.Column");
        protectedNames.Add("Grid.RowSpan");
        protectedNames.Add("Grid.ColumnSpan");
        protectedNames.Add("Width");
        protectedNames.Add("Height");
        protectedNames.Add("HorizontalAlignment");
        protectedNames.Add("VerticalAlignment");
        protectedNames.Add("Click");
        protectedNames.Add("Tapped");
        protectedNames.Add("Content");
        protectedNames.Add("Text");
        protectedNames.Add("DockPanel.Dock");
        protectedNames.Add("Canvas.Left");
        protectedNames.Add("Canvas.Top");
        foreach (var pair in node.ExtraAttributes)
            if (!protectedNames.Contains(pair.Key)) element.SetAttributeValue(ParseXName(pair.Key), pair.Value);
    }

    private static XDocument CreateBlankDocument(GridDocument model)
    {
        var root = new XElement(Ui + "Grid", new XAttribute(XNamespace.Xmlns + "x", X));
        if (!string.IsNullOrWhiteSpace(model.XClass)) root.SetAttributeValue(X + "Class", model.XClass);
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XElement FindEditableGrid(XElement documentRoot)
    {
        if (documentRoot.Name.LocalName is "Grid" or "FreedomPanel")
            return documentRoot;

        if (documentRoot.Name.LocalName is "Window" or "Page" or "UserControl" or "ContentPage")
        {
            var directSurface = documentRoot.Elements().FirstOrDefault(e =>
                e.Name.LocalName is "Grid" or "FreedomPanel");
            if (directSurface is not null)
                return directSurface;
        }

        throw new NotSupportedException(
            $"不支持的根布局：{documentRoot.Name.LocalName}。当前仅支持根 Grid/FreedomPanel，或 Window/Page/UserControl/ContentPage 的直接子 Grid/FreedomPanel。");
    }

    private static XElement? FindDefinitionsElement(XElement grid, bool row) => grid.Elements().FirstOrDefault(e => row ? IsRowDefinitionsElement(e) : IsColumnDefinitionsElement(e));
    private static bool IsRowDefinitionsElement(XElement e) => e.Name.LocalName == "Grid.RowDefinitions" || e.Name.LocalName == "RowDefinitions";
    private static bool IsColumnDefinitionsElement(XElement e) => e.Name.LocalName == "Grid.ColumnDefinitions" || e.Name.LocalName == "ColumnDefinitions";

    private static int ParseDefinitionAttribute(string? value, out bool sizingSupported)
    {
        sizingSupported = true;
        if (string.IsNullOrWhiteSpace(value)) return 1;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return 1;
        foreach (var part in parts)
            if (part != "*") sizingSupported = false;
        return Math.Max(1, parts.Length);
    }

    private static int ParseDefinitions(XElement? definitions, string elementName, string sizeAttribute, out bool sizingSupported)
    {
        sizingSupported = true;
        if (definitions is null) return 1;
        var count = 0;
        foreach (var def in definitions.Elements())
        {
            if (def.Name.LocalName != elementName) throw new NotSupportedException($"暂不支持 {def.Name.LocalName} 定义。");
            var value = (string?)def.Attribute(sizeAttribute);
            if (!string.IsNullOrWhiteSpace(value) && value != "*") sizingSupported = false;
            count++;
        }
        return Math.Max(1, count);
    }

    private static void UpdateDefinitions(XElement root, string groupName, string itemName, string sizeAttribute, int count)
    {
        var group = root.Elements().FirstOrDefault(e => e.Name.LocalName == groupName ||
            (groupName == "Grid.RowDefinitions" && e.Name.LocalName == "RowDefinitions") ||
            (groupName == "Grid.ColumnDefinitions" && e.Name.LocalName == "ColumnDefinitions"));
        if (group is null)
        {
            group = new XElement(Ui + groupName);
            var insertBefore = root.Elements().FirstOrDefault(e => !IsRowDefinitionsElement(e) && !IsColumnDefinitionsElement(e));
            if (insertBefore is null) root.Add(PrettyNewlineFor(root), group); else insertBefore.AddBeforeSelf(PrettyNewlineFor(root), group);
        }
        var items = group.Elements().Where(e => e.Name.LocalName == itemName).ToList();
        foreach (var item in items.Skip(count).Reverse()) item.Remove();
        items = items.Take(count).ToList();
        while (items.Count < count)
        {
            var item = new XElement(Ui + itemName, new XAttribute(sizeAttribute, "*"));
            group.Add(PrettyNewlineFor(group), item);
            items.Add(item);
        }
        foreach (var item in items.Take(count)) item.SetAttributeValue(sizeAttribute, "*");
    }

    private static XText PrettyNewlineFor(XElement parent)
    {
        var whitespace = parent.Nodes().OfType<XText>().Select(x => x.Value).FirstOrDefault(v => v.Contains('\n'));
        return new XText(whitespace ?? Environment.NewLine + "    ");
    }

    private static bool IsDesignableControl(string typeName)
    {
        return DesignableControlTypes.Contains(typeName);
    }

    private static XName ParseXName(string value)
    {
        if (value.StartsWith("{", StringComparison.Ordinal))
        {
            var close = value.IndexOf('}');
            if (close > 0 && close < value.Length - 1) return XName.Get(value[(close + 1)..], value[1..close]);
        }
        return XName.Get(value);
    }

    private static void SetOrRemove(XElement element, string name, string? value) => SetOrRemove(element, XName.Get(name), value);
    private static void SetOrRemove(XElement element, XName name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) element.Attribute(name)?.Remove(); else element.SetAttributeValue(name, value);
    }

    private static int IntAttr(XElement element, string name, int fallback = 0) => int.TryParse((string?)element.Attribute(name), out var value) ? value : fallback;

    private static double ParseDesignerOffset(XElement element, string absoluteName, string? margin, int index)
    {
        var absolute = (string?)element.Attribute(absoluteName);
        if (double.TryParse(absolute, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var coordinate))
            return coordinate;
        if (string.IsNullOrWhiteSpace(margin)) return 0;
        var parts = margin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sourceIndex = parts.Length >= 4 ? index : index;
        if (parts.Length == 1) return double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var all) ? all : 0;
        if (parts.Length >= 2) return double.TryParse(parts[sourceIndex], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        return 0;
    }

    private static string FormatMargin(double x, double y) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},0,0", x, y);
}
