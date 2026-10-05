using System.Text.Json;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageStack.Tests;

public class UserSettingsRepositoryTests
{
    private const string Alice = "alice-account-key";
    private const string Bob = "bob-account-key";

    // Every context of a test shares this root, so they see one database whatever else differs
    // between their options (the race test gives one of them an interceptor).
    private readonly InMemoryDatabaseRoot _root = new();

    private AppDbContext NewDb(string name, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name, _root)
            .AddInterceptors(interceptors)
            .Options);

    private static Dictionary<string, JsonElement> Changes(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static async Task<string?> ValueAsync(UserSettingsRepository repo, string account, string section, string key, CancellationToken ct)
    {
        var sections = await repo.GetAsync(account, ct);
        return sections.TryGetValue(section, out var saved) && saved.TryGetProperty(key, out var value)
            ? value.GetRawText()
            : null;
    }

    [Fact]
    public async Task Get_ForAnAccountThatNeverSaved_IsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(Guid.NewGuid().ToString());

        Assert.Empty(await new UserSettingsRepository(db).GetAsync(Alice, ct));
    }

    [Fact]
    public async Task Merge_CreatesTheSection_ThenKeepsTheKeysALaterSaveLeavesOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(Guid.NewGuid().ToString());
        var repo = new UserSettingsRepository(db);

        Assert.Equal(SettingsSaveResult.Saved, await repo.MergeAsync(Alice, "ui", Changes("""{"theme":"dark","locale":"nl"}"""), ct));
        Assert.Equal(SettingsSaveResult.Saved, await repo.MergeAsync(Alice, "ui", Changes("""{"theme":"light"}"""), ct));

        Assert.Equal("\"light\"", await ValueAsync(repo, Alice, "ui", "theme", ct));
        Assert.Equal("\"nl\"", await ValueAsync(repo, Alice, "ui", "locale", ct));
        var row = Assert.Single(await db.UserSettings.ToListAsync(ct));
        Assert.Equal(2, row.Version);
    }

    [Fact]
    public async Task Merge_KeepsAccountsAndSectionsApart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(Guid.NewGuid().ToString());
        var repo = new UserSettingsRepository(db);

        await repo.MergeAsync(Alice, "ui", Changes("""{"theme":"light"}"""), ct);
        await repo.MergeAsync(Alice, "map", Changes("""{"heatmapEnabled":false}"""), ct);
        await repo.MergeAsync(Bob, "ui", Changes("""{"theme":"dark"}"""), ct);

        var alice = await repo.GetAsync(Alice, ct);
        Assert.Equal(["map", "ui"], alice.Keys.Order());
        Assert.Equal("\"light\"", await ValueAsync(repo, Alice, "ui", "theme", ct));
        Assert.Equal("\"dark\"", await ValueAsync(repo, Bob, "ui", "theme", ct));
        Assert.Null(await ValueAsync(repo, Bob, "map", "heatmapEnabled", ct));
    }

    [Fact]
    public async Task Merge_ThatWouldGrowTheSectionTooLarge_IsRefused_AndKeepsWhatWasSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(Guid.NewGuid().ToString());
        var repo = new UserSettingsRepository(db);
        await repo.MergeAsync(Alice, "map", Changes("""{"heatmapEnabled":false}"""), ct);
        var huge = new string('x', UserSettingsLimits.JsonMaxLength);

        var result = await repo.MergeAsync(Alice, "map", Changes($$"""{"fuelBrandFilter":["{{huge}}"]}"""), ct);

        Assert.Equal(SettingsSaveResult.TooLarge, result);
        Assert.Null(await ValueAsync(repo, Alice, "map", "fuelBrandFilter", ct));
        Assert.Equal("false", await ValueAsync(repo, Alice, "map", "heatmapEnabled", ct));
    }

    [Fact]
    public async Task Merge_ThatRacedAnotherDevice_KeepsBothDevicesKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Guid.NewGuid().ToString();
        await using (var setup = NewDb(name))
            await new UserSettingsRepository(setup).MergeAsync(Alice, "ui", Changes("""{"theme":"dark"}"""), ct);

        // The phone saves its locale in between this device reading the row and writing it back.
        var race = new RaceBeforeSaves(times: 1, async _ =>
        {
            await using var phone = NewDb(name);
            await new UserSettingsRepository(phone).MergeAsync(Alice, "ui", Changes("""{"locale":"nl"}"""), ct);
        });
        await using var db = NewDb(name, race);

        var result = await new UserSettingsRepository(db).MergeAsync(Alice, "ui", Changes("""{"theme":"light"}"""), ct);

        Assert.Equal(SettingsSaveResult.Saved, result);
        await using var check = NewDb(name);
        var repo = new UserSettingsRepository(check);
        Assert.Equal("\"light\"", await ValueAsync(repo, Alice, "ui", "theme", ct));
        Assert.Equal("\"nl\"", await ValueAsync(repo, Alice, "ui", "locale", ct));
    }

    [Fact]
    public async Task Merge_ThatLosesEveryRace_GivesUp_AndLeavesTheWinnersSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Guid.NewGuid().ToString();
        await using (var setup = NewDb(name))
            await new UserSettingsRepository(setup).MergeAsync(Alice, "ui", Changes("""{"theme":"dark"}"""), ct);

        // Another device gets in first every single time.
        var race = new RaceBeforeSaves(times: int.MaxValue, async round =>
        {
            await using var other = NewDb(name);
            await new UserSettingsRepository(other).MergeAsync(Alice, "ui", Changes($$"""{"round{{round}}":true}"""), ct);
        });
        await using var db = NewDb(name, race);

        var result = await new UserSettingsRepository(db).MergeAsync(Alice, "ui", Changes("""{"theme":"light"}"""), ct);

        Assert.Equal(SettingsSaveResult.Conflict, result);
        await using var check = NewDb(name);
        var repo = new UserSettingsRepository(check);
        Assert.Equal("\"dark\"", await ValueAsync(repo, Alice, "ui", "theme", ct));
        Assert.Equal("true", await ValueAsync(repo, Alice, "ui", "round1", ct));
    }

    [Fact]
    public async Task Get_AnUnreadableSection_IsServedEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(Guid.NewGuid().ToString());
        db.UserSettings.Add(new UserSettings { AccountKey = Alice, Section = "ui", Json = "{broken" });
        await db.SaveChangesAsync(ct);

        var sections = await new UserSettingsRepository(db).GetAsync(Alice, ct);

        Assert.Empty(sections["ui"].EnumerateObject());
    }

    /// <summary>
    /// Runs <paramref name="race"/> just before each of the first <paramref name="times"/> saves it
    /// sees go out, passing the round (1, 2, ...).
    /// </summary>
    private sealed class RaceBeforeSaves(int times, Func<int, Task> race) : SaveChangesInterceptor
    {
        private int _rounds;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_rounds < times) await race(++_rounds);
            return result;
        }
    }
}
