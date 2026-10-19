using System.Text.Json;
using GarageStack.Core.Models;
using GarageStack.Data.Repositories;

namespace GarageStack.Tests;

// Devices saving the same settings section at the same moment, on a database that really refuses
// the losing write: the version column and the retry are what keep every device's keys.
public class UserSettingsRepositoryPostgresTests
{
    private static Dictionary<string, JsonElement> Change(string key, string value) =>
        new() { [key] = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public async Task SavesAtTheSameMoment_KeepEveryDevicesKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        const int devices = 8;

        var saves = Enumerable.Range(0, devices).Select(async i =>
        {
            await using var db = database.CreateContext();
            return await new UserSettingsRepository(db).MergeAsync("account", "ui", Change($"key{i}", $"value{i}"), ct);
        });
        var results = await Task.WhenAll(saves);

        await using var check = database.CreateContext();
        var saved = (await new UserSettingsRepository(check).GetAsync("account", ct))["ui"];
        Assert.All(results, r => Assert.Equal(SettingsSaveResult.Saved, r));
        Assert.Equal(devices, saved.EnumerateObject().Count());
    }
}
