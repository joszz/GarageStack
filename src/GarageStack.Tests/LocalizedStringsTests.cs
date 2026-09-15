using System.Globalization;
using GarageStack.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageStack.Tests;

/// <summary>
/// Resolves the resx-backed localizers the same way the hosts do. A marker type in the wrong
/// namespace makes the localizer silently return the resource keys, which for English keys
/// that equal their values is invisible, so these tests always assert on the Dutch texts.
/// </summary>
public class LocalizedStringsTests
{
    private static IStringLocalizer<T> Resolve<T>() =>
        new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<T>>();

    private static T WithUiCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try { return action(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void NotificationStrings_ResolveEnglishByDefault()
    {
        var strings = WorkerLocalizer.Notifications();

        var title = WithUiCulture("en", () => strings["EngineStartTitle"]);

        Assert.False(title.ResourceNotFound);
        Assert.Equal("Engine started", title.Value);
    }

    [Fact]
    public void NotificationStrings_ResolveDutch_WhenUiCultureIsDutch()
    {
        var strings = WorkerLocalizer.Notifications();

        var (title, body) = WithUiCulture("nl", () => (strings["EngineStartTitle"], strings["DoorsOpenBody", "bestuurder"]));

        Assert.Equal("Motor gestart", title.Value);
        Assert.Equal("Portier(en) open terwijl geparkeerd: bestuurder", body.Value);
    }

    [Fact]
    public void WidgetStrings_ResolveDutch_WhenUiCultureIsDutch()
    {
        var strings = Resolve<WidgetStrings>();

        var locked = WithUiCulture("nl", () => strings["Locked"]);

        Assert.False(locked.ResourceNotFound);
        Assert.Equal("Vergrendeld", locked.Value);
    }
}
