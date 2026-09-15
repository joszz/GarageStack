namespace GarageStack.Api;

/// <summary>
/// Marker type for the widget endpoint's localized strings (Resources/WidgetStrings.resx).
/// It has to live in the assembly's root namespace: the resource localizer maps a marker type
/// to a resource by stripping the root namespace from the type's full name and prefixing the
/// ResourcesPath, so a marker in a sub-namespace would look for Resources/Endpoints/WidgetStrings
/// and silently fall back to the resource keys.
/// </summary>
public sealed class WidgetStrings;
