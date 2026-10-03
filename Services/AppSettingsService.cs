using Windows.Storage;

namespace XamlSub.Services;

public sealed class AppSettingsService
{
    private const string BackdropKey = "WindowBackdrop";
    private const string PositioningModeKey = "PositioningMode";
    private const string RelativeStrategyKey = "RelativeStrategy";
    private const string AbsoluteStrategyKey = "AbsoluteStrategy";
    private const string AutoSaveKey = "AutoSaveEnabled";
    private const string BroadSaveCheckKey = "BroadSaveCheckEnabled";
    private const string HighFidelityPreviewKey = "HighFidelityPreviewEnabled";

    private readonly ApplicationDataContainer _localSettings;

    public const string Mica = "Mica";
    public const string Acrylic = "Acrylic";
    public const string None = "None";

    public const string Relative = "相对定位";
    public const string Absolute = "绝对坐标";

    // Legacy persisted value. Keep this stable so existing user settings continue to work.
    public const string CustomPanel = "自定义 Panel";
    public const string RelativeFreeDisplayName = "FreedomPanel";
    public const string DockPanel = "DockPanel";
    public const string Grid = "Grid";

    public const string Canvas = "Canvas";
    public const string GridNegativeMargin = "Grid 中使用 Margin 负值";
    public const string GridExact = "Grid 中使用精确值";

    public AppSettingsService()
    {
        _localSettings = ApplicationData.Current.LocalSettings;
    }

    public string WindowBackdrop
    {
        get => Read(BackdropKey, Mica);
        set => Write(BackdropKey, value);
    }

    public string PositioningMode
    {
        get => Read(PositioningModeKey, Relative);
        set => Write(PositioningModeKey, value);
    }

    public string RelativeStrategy
    {
        get => Read(RelativeStrategyKey, CustomPanel);
        set => Write(RelativeStrategyKey, value);
    }

    public string AbsoluteStrategy
    {
        get => Read(AbsoluteStrategyKey, Canvas);
        set => Write(AbsoluteStrategyKey, value);
    }

    public bool AutoSaveEnabled
    {
        get => ReadBool(AutoSaveKey, false);
        set => _localSettings.Values[AutoSaveKey] = value;
    }

    public bool BroadSaveCheckEnabled
    {
        get => ReadBool(BroadSaveCheckKey, false);
        set => _localSettings.Values[BroadSaveCheckKey] = value;
    }

    public bool HighFidelityPreviewEnabled
    {
        get => ReadBool(HighFidelityPreviewKey, false);
        set => _localSettings.Values[HighFidelityPreviewKey] = value;
    }

    private string Read(string key, string fallback)
    {
        var value = _localSettings.Values[key]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private bool ReadBool(string key, bool fallback)
    {
        var value = _localSettings.Values[key];
        return value is bool boolean ? boolean : fallback;
    }

    private void Write(string key, string value)
    {
        _localSettings.Values[key] = value;
    }
}
