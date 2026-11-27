using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

// What a device switched off for one car arrives from the browser and is stored as JSON, so an
// unchanged choice has to come out the same however the page listed it.
public class VehicleCategoryExclusionsTests
{
    private static Dictionary<int, IReadOnlyList<string>> Request(params (int Vehicle, string[] Types)[] entries) =>
        entries.ToDictionary(e => e.Vehicle, e => (IReadOnlyList<string>)e.Types);

    [Fact]
    public void Normalize_KeepsKnownTypesOnceEachInOneOrder()
    {
        var result = VehicleCategoryExclusions.Normalize(Request((2, ["low-tyre", "engine-start", "not-a-type", "low-tyre"])));

        Assert.Equal(["engine-start", "low-tyre"], result[2]);
    }

    // A car with nothing off needs no entry: it is the same as a car the device never mentioned.
    [Fact]
    public void Normalize_DropsCarsWithNoKnownTypeOff()
    {
        var result = VehicleCategoryExclusions.Normalize(Request((1, []), (2, ["not-a-type"]), (3, ["low-tyre"])));

        Assert.Equal([3], result.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Normalize_DropsAVehicleIdNoCarHas(int vehicleId)
    {
        Assert.Empty(VehicleCategoryExclusions.Normalize(Request((vehicleId, ["low-tyre"]))));
    }

    // The map comes from the browser and every push reads it, so it is held to a size.
    [Fact]
    public void Normalize_KeepsTheLowestIdsWhenThereAreMoreCarsThanAnAccountHas()
    {
        var many = Request(Enumerable.Range(1, VehicleCategoryExclusions.MaxVehicles + 10)
            .Select(id => (id, new[] { "low-tyre" })).ToArray());

        var result = VehicleCategoryExclusions.Normalize(many);

        Assert.Equal(VehicleCategoryExclusions.MaxVehicles, result.Count);
        Assert.Contains(1, result.Keys);
        Assert.DoesNotContain(VehicleCategoryExclusions.MaxVehicles + 1, result.Keys);
    }

    [Fact]
    public void AreEqual_ComparesTheTypesPerCar()
    {
        var a = new Dictionary<int, List<string>> { [1] = ["low-tyre"], [2] = ["engine-start"] };

        Assert.True(VehicleCategoryExclusions.AreEqual(a, new Dictionary<int, List<string>> { [2] = ["engine-start"], [1] = ["low-tyre"] }));
        Assert.False(VehicleCategoryExclusions.AreEqual(a, new Dictionary<int, List<string>> { [1] = ["low-tyre"] }));
        Assert.False(VehicleCategoryExclusions.AreEqual(a, new Dictionary<int, List<string>> { [1] = ["low-tyre"], [2] = ["low-fuel"] }));
        Assert.False(VehicleCategoryExclusions.AreEqual(a, new Dictionary<int, List<string>> { [1] = ["low-tyre"], [3] = ["engine-start"] }));
    }

    [Fact]
    public void Json_RoundTrips_AndWritesTheSameTextForTheSameChoice()
    {
        var choice = VehicleCategoryExclusions.Normalize(Request((9, ["low-tyre", "engine-start"]), (4, ["maintenance"])));

        var json = VehicleCategoryExclusions.ToJson(choice);

        Assert.Equal("""{"4":["maintenance"],"9":["engine-start","low-tyre"]}""", json);
        Assert.True(VehicleCategoryExclusions.AreEqual(choice, VehicleCategoryExclusions.FromJson(json)));
    }

    // A column that was never written, or holds something else, must not stop a notification going out.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"abc":["low-tyre"]}""")]
    [InlineData("[1,2]")]
    public void FromJson_AnythingUnreadable_MeansNoExclusions(string? json)
    {
        Assert.Empty(VehicleCategoryExclusions.FromJson(json));
    }
}
