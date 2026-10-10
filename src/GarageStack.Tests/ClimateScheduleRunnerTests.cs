using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

/// <summary>
/// A Worker with its database in memory and a fake line to the car: what climate schedules need to
/// run in a test. Every scope shares one database.
/// </summary>
internal sealed class ClimateScheduleTestWorker : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 5, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;

    public FixedTimeProvider Clock { get; } = new(Now);
    public FakeCommandClient Car { get; } = new();
    public FakePushSender Push { get; } = new();
    public IServiceScopeFactory Scopes { get; }
    public ClimateScheduleRunner Runner { get; }

    public ClimateScheduleTestWorker()
    {
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString();
        _services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name, root))
            .AddScoped<ITelemetryRepository>(sp => new TelemetryRepository(sp.GetRequiredService<AppDbContext>()))
            .AddScoped<IClimateScheduleRepository, ClimateScheduleRepository>()
            .BuildServiceProvider();
        Scopes = _services.GetRequiredService<IServiceScopeFactory>();
        Runner = new ClimateScheduleRunner(
            Scopes, Car, Push, WorkerLocalizer.Notifications(), Clock, NullLogger<ClimateScheduleRunner>.Instance);
    }

    public ClimateScheduleService Service() =>
        new(NullLogger<ClimateScheduleService>.Instance, Scopes, Runner, Clock);

    // The one car every schedule and reading belongs to, added on first use.
    private static async Task<Vehicle> VehicleAsync(AppDbContext db, string? saicUser = "user")
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        if (vehicle is not null) return vehicle;

        vehicle = new Vehicle { Vin = "FAKEVN00000000001", SaicUser = saicUser };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return vehicle;
    }

    public async Task<ClimateSchedule> AddScheduleAsync(Action<ClimateSchedule>? configure = null, string? saicUser = "user")
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vehicle = await VehicleAsync(db, saicUser);

        var schedule = new ClimateSchedule
        {
            Vehicle = vehicle,
            StartTime = new TimeOnly(7, 30),
            Days = ClimateScheduleDays.Friday,
            TimeZoneId = "Europe/Amsterdam",
            Mode = ClimateScheduleMode.On,
            TemperatureC = 21,
            NextRunUtc = Now.UtcDateTime,
        };
        configure?.Invoke(schedule);
        db.ClimateSchedules.Add(schedule);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return schedule;
    }

    public async Task AddTelemetryAsync(TimeSpan ago, bool? engineRunning = null, double? exteriorC = null)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vehicle = await VehicleAsync(db);
        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = Now.UtcDateTime - ago,
            EngineRunning = engineRunning,
            ExteriorTemperature = exteriorC,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async Task<ClimateSchedule> ReadAsync(int id)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ClimateSchedules.AsNoTracking().Include(s => s.Vehicle)
            .SingleAsync(s => s.Id == id, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _services.Dispose();
}

/// <summary>A car that answers every command as told, and remembers what it was sent.</summary>
internal sealed class FakeCommandClient : IVehicleCommandClient
{
    public List<(string Command, string Value)> Sent { get; } = [];

    /// <summary>The answer per command; one not listed succeeds.</summary>
    public Dictionary<string, CommandAnswer> Answers { get; } = [];

    /// <summary>When set, every command waits for it before answering.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public async Task<CommandAnswer> SendAsync(string vin, string command, string value, CancellationToken ct)
    {
        lock (Sent) Sent.Add((command, value));
        if (Hold is { } hold) await hold.Task.WaitAsync(ct);
        return Answers.GetValueOrDefault(command, new CommandAnswer(true, true, null));
    }
}

public class ClimateScheduleRunnerTests : IDisposable
{
    private readonly ClimateScheduleTestWorker _worker = new();

    public void Dispose() => _worker.Dispose();

    private async Task<ClimateSchedule> RunAsync(Action<ClimateSchedule>? configure = null, string? saicUser = "user")
    {
        var schedule = await _worker.AddScheduleAsync(configure, saicUser);
        await _worker.Runner.RunAsync(await _worker.ReadAsync(schedule.Id), TestContext.Current.CancellationToken);
        return await _worker.ReadAsync(schedule.Id);
    }

    [Fact]
    public async Task AllCommandsTaken_IsStarted_AndTellsTheUser()
    {
        var after = await RunAsync(s => { s.Name = "Work"; s.SeatLeftLevel = 2; });

        Assert.Equal([("climate-temperature", "21"), ("climate", "on"), ("seat-left", "2")], _worker.Car.Sent);
        Assert.Equal(ClimateScheduleOutcome.Started, after.LastRunOutcome);
        Assert.Null(after.LastRunFailedCommand);
        var push = Assert.Single(_worker.Push.Sent);
        Assert.Equal(NotificationCategories.ClimateSchedule, push.Category);
        Assert.Equal("Climate is on", push.Title);
        Assert.Equal("Started by your schedule \"Work\" (07:30).", push.Body);
    }

    [Fact]
    public async Task ClimateRefused_IsFailed_AndSendsNothingAfterIt()
    {
        _worker.Car.Answers["climate"] = new CommandAnswer(true, false, "vehicle offline");

        var after = await RunAsync(s => s.RearDefroster = true);

        Assert.Equal([("climate-temperature", "21"), ("climate", "on")], _worker.Car.Sent);
        Assert.Equal(ClimateScheduleOutcome.Failed, after.LastRunOutcome);
        Assert.Equal("climate", after.LastRunFailedCommand);
        Assert.Equal("vehicle offline", after.LastRunDetail);
        var push = Assert.Single(_worker.Push.Sent);
        Assert.Equal("Climate did not start", push.Title);
        Assert.Equal("The car refused your 07:30 schedule: vehicle offline", push.Body);
    }

    [Fact]
    public async Task NoAnswer_IsUnconfirmed()
    {
        _worker.Car.Answers["climate"] = CommandAnswer.NoAnswer;

        var after = await RunAsync();

        Assert.Equal(ClimateScheduleOutcome.Unconfirmed, after.LastRunOutcome);
        Assert.Equal("Climate may not have started", Assert.Single(_worker.Push.Sent).Title);
    }

    [Fact]
    public async Task AnExtraFailingAfterClimateStarted_IsStillStarted_AndNamesTheExtra()
    {
        _worker.Car.Answers["seat-right"] = new CommandAnswer(true, false, "seat heating unavailable");

        var after = await RunAsync(s => s.SeatRightLevel = 1);

        Assert.Equal(ClimateScheduleOutcome.Started, after.LastRunOutcome);
        Assert.Equal("seat-right", after.LastRunFailedCommand);
        Assert.Equal("Started by your 07:30 schedule, but passenger seat heating did not work.",
            Assert.Single(_worker.Push.Sent).Body);
    }

    [Fact]
    public async Task ACarBeingDriven_IsSkipped_WithoutCommandsOrPush()
    {
        await _worker.AddTelemetryAsync(TimeSpan.FromMinutes(1), engineRunning: true);

        var after = await RunAsync();

        Assert.Empty(_worker.Car.Sent);
        Assert.Empty(_worker.Push.Sent);
        Assert.Equal(ClimateScheduleOutcome.SkippedDriving, after.LastRunOutcome);
    }

    [Fact]
    public async Task ARecentMildReading_SkipsAColdOnlySchedule()
    {
        await _worker.AddTelemetryAsync(TimeSpan.FromHours(1), exteriorC: 12);

        var after = await RunAsync(s => s.OnlyBelowC = 5);

        Assert.Empty(_worker.Car.Sent);
        Assert.Empty(_worker.Push.Sent);
        Assert.Equal(ClimateScheduleOutcome.SkippedTemperature, after.LastRunOutcome);
    }

    [Fact]
    public async Task AnOldMildReading_DoesNotDecideTheRun()
    {
        await _worker.AddTelemetryAsync(TimeSpan.FromHours(5), exteriorC: 12);

        var after = await RunAsync(s => s.OnlyBelowC = 5);

        Assert.Equal(ClimateScheduleOutcome.Started, after.LastRunOutcome);
    }

    [Fact]
    public async Task ACarWhoseAccountIsNotKnownYet_FailsWithoutSendingAnything()
    {
        var after = await RunAsync(saicUser: null);

        Assert.Empty(_worker.Car.Sent);
        Assert.Equal(ClimateScheduleOutcome.Failed, after.LastRunOutcome);
        Assert.Equal("The car refused your 07:30 schedule.", Assert.Single(_worker.Push.Sent).Body);
    }
}

public class ClimateScheduleServiceTests : IDisposable
{
    private readonly ClimateScheduleTestWorker _worker = new();

    public void Dispose() => _worker.Dispose();

    private async Task PassAsync(ClimateScheduleService service)
    {
        await service.RunPassAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(service.StartedRuns);
    }

    [Fact]
    public async Task ADueRepeatingSchedule_RunsAndMovesOnToItsNextDay()
    {
        var schedule = await _worker.AddScheduleAsync();

        await PassAsync(_worker.Service());

        var after = await _worker.ReadAsync(schedule.Id);
        Assert.True(after.Enabled);
        // Friday 07:30 in Amsterdam, a week on.
        Assert.Equal(new DateTime(2026, 10, 16, 5, 30, 0), after.NextRunUtc);
        Assert.Equal(ClimateScheduleTestWorker.Now.UtcDateTime, after.LastRunAt);
        Assert.Equal(ClimateScheduleOutcome.Started, after.LastRunOutcome);
    }

    [Fact]
    public async Task ADueOneOffSchedule_RunsAndSwitchesItselfOff()
    {
        var schedule = await _worker.AddScheduleAsync(s => s.Days = ClimateScheduleDays.None);

        await PassAsync(_worker.Service());

        var after = await _worker.ReadAsync(schedule.Id);
        Assert.False(after.Enabled);
        Assert.Null(after.NextRunUtc);
        Assert.Equal(ClimateScheduleOutcome.Started, after.LastRunOutcome);
    }

    [Fact]
    public async Task ARunFoundLongAfterItWasDue_IsMissed_WithoutCommandsOrPush()
    {
        var dueAt = ClimateScheduleTestWorker.Now.UtcDateTime;
        var schedule = await _worker.AddScheduleAsync();
        _worker.Clock.Advance(TimeSpan.FromMinutes(20));

        await PassAsync(_worker.Service());

        var after = await _worker.ReadAsync(schedule.Id);
        Assert.Empty(_worker.Car.Sent);
        Assert.Empty(_worker.Push.Sent);
        Assert.Equal(ClimateScheduleOutcome.Missed, after.LastRunOutcome);
        Assert.Equal(dueAt, after.LastRunAt);
        Assert.Equal(new DateTime(2026, 10, 16, 5, 30, 0), after.NextRunUtc);
    }

    [Fact]
    public async Task ARunCutShortByARestart_IsUnconfirmedOnTheFirstPass()
    {
        var schedule = await _worker.AddScheduleAsync(s =>
        {
            s.NextRunUtc = ClimateScheduleTestWorker.Now.UtcDateTime.AddDays(7);
            s.LastRunOutcome = ClimateScheduleOutcome.Running;
        });

        await PassAsync(_worker.Service());

        Assert.Equal(ClimateScheduleOutcome.Unconfirmed, (await _worker.ReadAsync(schedule.Id)).LastRunOutcome);
    }

    [Fact]
    public async Task ASecondScheduleForACarStillRunning_WaitsForTheNextPass()
    {
        var first = await _worker.AddScheduleAsync();
        var second = await _worker.AddScheduleAsync(s => s.StartTime = new TimeOnly(7, 31));
        _worker.Car.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = _worker.Service();

        await service.RunPassAsync(TestContext.Current.CancellationToken);
        var waiting = await _worker.ReadAsync(second.Id);
        _worker.Car.Hold.SetResult();
        await Task.WhenAll(service.StartedRuns);

        Assert.Equal(ClimateScheduleOutcome.Started, (await _worker.ReadAsync(first.Id)).LastRunOutcome);
        Assert.Null(waiting.LastRunOutcome);
        Assert.Equal(ClimateScheduleTestWorker.Now.UtcDateTime, waiting.NextRunUtc);
    }
}
