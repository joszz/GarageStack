using System.Text.Json;
using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class SettingsDocumentTests
{
    private static Dictionary<string, JsonElement> Changes(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public void Merge_WritesTheChangesOverTheStoredKeys_AndKeepsTheRest()
    {
        var merged = SettingsDocument.Merge(
            """{"theme":"dark","locale":"nl","units":{"distance":"km","pressure":"bar"}}""",
            Changes("""{"theme":"light","units":{"distance":"mi"}}"""));

        using var document = JsonDocument.Parse(merged);
        var root = document.RootElement;
        Assert.Equal("light", root.GetProperty("theme").GetString());
        Assert.Equal("nl", root.GetProperty("locale").GetString());
        // A key's value is replaced whole, never merged into: the browser always sends it whole.
        Assert.Equal("""{"distance":"mi"}""", root.GetProperty("units").GetRawText());
    }

    [Fact]
    public void Merge_IntoNothingStored_IsJustTheChanges()
    {
        var merged = SettingsDocument.Merge(null, Changes("""{"cards":[{"id":"doors","visible":false}],"showTyreDiagram":null}"""));

        Assert.Equal("""{"cards":[{"id":"doors","visible":false}],"showTyreDiagram":null}""", merged);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    public void Read_SomethingOtherThanAnObject_IsEmpty_AndSaysSo(string stored)
    {
        Exception? reported = null;

        var read = SettingsDocument.Read(stored, ex => reported = ex);

        Assert.Equal(JsonValueKind.Object, read.ValueKind);
        Assert.Empty(read.EnumerateObject());
        Assert.NotNull(reported);
    }

    [Fact]
    public void Merge_OverAnUnreadableSection_StartsItAfresh()
    {
        Assert.Equal("""{"theme":"light"}""", SettingsDocument.Merge("{broken", Changes("""{"theme":"light"}""")));
    }
}
