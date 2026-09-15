namespace GarageStack.Worker;

/// <summary>
/// Marker type for the push notification texts in Resources/NotificationStrings*.resx. Kept in
/// the assembly's root namespace on purpose: the resource localizer derives the resource name
/// by stripping the root namespace from the marker's full name, so a marker in a sub-namespace
/// would look for a resource that does not exist and silently fall back to the keys.
/// </summary>
public sealed class NotificationStrings;
