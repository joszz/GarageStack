using GarageStack.Core.Models;
using GarageStack.Data.Repositories;

namespace GarageStack.Tests;

// A car's settings live in a table of their own: the first save adds the row, a later one
// changes it in place, and a read of the car brings them along.
public class VehicleRepositoryPostgresTests
{
    [Fact]
    public async Task Settings_SavedTwice_AreReadBackWithTheCar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
        {
            var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync(ct);
        }

        await using (var first = database.CreateContext())
        {
            var repository = new VehicleRepository(first);
            var car = (await repository.GetByVinAsync("FAKEVN00000000001", ct))!;
            Assert.Null(car.Settings);
            await repository.SetSettingsAsync(car.Id, new VehicleSettings { TyreLowBar = 2.4, VehicleType = "phev" }, ct);
        }

        await using (var second = database.CreateContext())
        {
            var repository = new VehicleRepository(second);
            var car = (await repository.GetByVinAsync("FAKEVN00000000001", ct))!;
            await repository.SetSettingsAsync(car.Id, new VehicleSettings { TyreLowBar = null, LowFuelPercent = 10, VehicleType = "phev" }, ct);
        }

        await using var check = database.CreateContext();
        var saved = Assert.Single(await new VehicleRepository(check).GetAllAsync(ct)).Settings;
        Assert.NotNull(saved);
        Assert.Null(saved.TyreLowBar);
        Assert.Equal(10, saved.LowFuelPercent);
        Assert.Equal("phev", saved.VehicleType);
    }
}
