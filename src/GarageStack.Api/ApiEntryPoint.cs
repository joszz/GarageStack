namespace GarageStack.Api;

/// <summary>
/// Marker type for <c>WebApplicationFactory&lt;T&gt;</c> in the test project: the factory only
/// uses it to find the assembly holding the entry point. Top-level statements put Program in the
/// global namespace, and both this app and the Worker have one, so a test assembly that sees the
/// internals of both cannot name either unambiguously.
/// </summary>
public sealed class ApiEntryPoint;
