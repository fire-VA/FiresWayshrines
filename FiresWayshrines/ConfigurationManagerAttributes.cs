using System;
using BepInEx.Configuration;

/// <summary>
/// Standard BepInEx Configuration Manager attribute holder. Configuration Manager reads these fields by name,
/// so the class stays in the global namespace with the full field set.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    public delegate void CustomHotkeyDrawerFunc(ConfigEntryBase setting, ref bool isCurrentlyAcceptingInput);

    public bool? ShowRangeAsPercent;
    public Action<ConfigEntryBase> CustomDrawer;
    public CustomHotkeyDrawerFunc CustomHotkeyDrawer;
    public bool? Browsable;
    public string Category;
    public object DefaultValue;
    public bool? HideDefaultButton;
    public bool? HideSettingName;
    public string Description;
    public string DispName;
    public int? Order;
    public bool? ReadOnly;
    public bool? IsAdvanced;
    public Func<object, string> ObjToStr;
    public Func<string, object> StrToObj;
}
