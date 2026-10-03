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
        "Click", "Tapped", "Loaded"
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

    public GridDocument Load(string path)
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
            XClass = (string?)documentRoot.Attribute(X + "Class"),
            RootXName = (string?)grid.Attribute(X + "Name")
        };

        foreach (var attr in documentRoot.Attributes())
        {
            if (attr.IsNamespaceDeclaration || attr.Name == X + "Class") continue;
            if (RootEditableProperties.Contains(attr.Name.LocalName)) model.RootProperties[attr.Name.LocalName] = attr.Value;
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
                EventName = (string?)element.Attribute("Click") ?? (string?)element.Attribute("Tapped"),
                OffsetX = ParseDesignerOffset(element, "Canvas.Left", (string?)element.Attribute("Margin"), 0),
                OffsetY = ParseDesignerOffset(element, "Canvas.Top", (string?)element.Attribute("Margin"), 1),
                Dock = (string?)element.Attribute("DockPanel.Dock"),
                DesignerPositionCustomized = false
            };
            node.EventKind = element.Attribute("Click") is not null ? "Click" : element.Attribute("Tapped") is not null ? "Tapped" : null;
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

        SetOrRemove(documentRoot, X + "Class", model.XClass);
        foreach (var property in RootEditableProperties)
            SetOrRemove(documentRoot, property, model.RootProperties.TryGetValue(property, out var value) ? value : null);
        SetOrRemove(root, X + "Name", model.RootXName);

        if (model.GridSizingSupported)
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

    private static void ApplyNodeAttributes(XElement element, ControlNode node, GridDocument model)
    {
        SetOrRemove(element, X + "Name", node.XName);
        var usesText = node.TypeName == "TextBlock" || node.TypeName == "TextBox" || node.TypeName == "PasswordBox" || node.TypeName == "RichEditBox" || node.TypeName == "AutoSuggestBox";
        var usesContent = node.TypeName == "Button" || node.TypeName == "HyperlinkButton" || node.TypeName == "ToggleButton" || node.TypeName == "RepeatButton" || node.TypeName == "CheckBox" || node.TypeName == "RadioButton" || node.TypeName == "Expander";
        SetOrRemove(element, "Content", usesContent ? node.Content : null);
        SetOrRemove(element, "Text", usesText ? node.Content : null);
        var freeRelative = model.RelativeFreePositioning;
        var rowText = freeRelative ? "0" : node.Row.ToString();
        var columnText = freeRelative ? "0" : node.Column.ToString();
        string? rowSpanText;
        string? columnSpanText;
        if (freeRelative)
        {
            rowSpanText = model.Rows > 1 ? model.Rows.ToString() : null;
            columnSpanText = model.Columns > 1 ? model.Columns.ToString() : null;
        }
        else
        {
            rowSpanText = node.RowSpan > 1 ? node.RowSpan.ToString() : null;
            columnSpanText = node.ColumnSpan > 1 ? node.ColumnSpan.ToString() : null;
        }

        SetOrRemove(element, "Grid.Row", rowText);
        SetOrRemove(element, "Grid.Column", columnText);
        SetOrRemove(element, "Grid.RowSpan", rowSpanText);
        SetOrRemove(element, "Grid.ColumnSpan", columnSpanText);
        SetOrRemove(element, "Width", node.Width);
        SetOrRemove(element, "Height", node.Height);
        SetOrRemove(element, "HorizontalAlignment", freeRelative ? "Left" : node.HorizontalAlignment);
        SetOrRemove(element, "VerticalAlignment", freeRelative ? "Top" : node.VerticalAlignment);
        if (freeRelative || node.DesignerPositionCustomized)
            SetOrRemove(element, "Margin", FormatMargin(node.OffsetX, node.OffsetY));
        if (!string.IsNullOrWhiteSpace(node.Dock)) SetOrRemove(element, "DockPanel.Dock", node.Dock);
        else element.Attribute("DockPanel.Dock")?.Remove();

        foreach (var name in new[] { "Click", "Tapped" }) element.Attribute(name)?.Remove();
        if (!string.IsNullOrWhiteSpace(node.EventName)) element.SetAttributeValue(node.EventKind ?? node.DerivedEventKind, node.EventName);

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

        if (freeRelative)
        {
            // Canonical serialization: one parent-relative origin and exactly one
            // positioning representation. This intentionally replaces other legal
            // Grid alignment/margin combinations instead of trying to preserve all
            // equivalent spellings.
            SetOrRemove(element, "HorizontalAlignment", "Left");
            SetOrRemove(element, "VerticalAlignment", "Top");
            SetOrRemove(element, "Margin", FormatMargin(node.OffsetX, node.OffsetY));
            SetOrRemove(element, "Canvas.Left", null);
            SetOrRemove(element, "Canvas.Top", null);
            SetOrRemove(element, "DockPanel.Dock", null);
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
        if (documentRoot.Name.LocalName == "Grid") return documentRoot;
        if (documentRoot.Name.LocalName == "Window" || documentRoot.Name.LocalName == "Page" || documentRoot.Name.LocalName == "UserControl")
        {
            var directGrid = documentRoot.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid");
            if (directGrid is not null) return directGrid;
        }
        throw new NotSupportedException($"不支持的根布局：{documentRoot.Name.LocalName}。当前仅支持根 Grid，或 Window/Page/UserControl 的直接子 Grid。");
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
