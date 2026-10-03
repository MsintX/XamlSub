namespace XamlSub.Models;

public sealed class ToolboxItem
{
    public string TypeName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ToolboxItem() { }
    public ToolboxItem(string typeName, string displayName)
    {
        TypeName = typeName;
        DisplayName = displayName;
    }
}
