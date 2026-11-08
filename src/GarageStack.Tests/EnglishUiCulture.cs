using System.Globalization;
using System.Runtime.CompilerServices;

namespace GarageStack.Tests;

/// <summary>Keep resource-backed tests deterministic across developer machines with different UI languages.</summary>
internal static class EnglishUiCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var english = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }
}
