using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Worker;
using GarageStack.Worker.Services;
using Microsoft.Extensions.Localization;

namespace GarageStack.Tests;

// FakePushSender / FakeServiceScopeFactory live in WorkerTestFakes.cs.

public class MaintenanceCheckServiceTests
{
    private static readonly IStringLocalizer<NotificationStrings> Strings = WorkerLocalizer.Notifications();

    private static MaintenanceItem Item(int id = 1, string name = "Oil change") =>
        new() { Id = id, VehicleId = 1, Name = name };

    [Fact]
    public void BuildAlert_Ok_ReturnsNull()
    {
        var result = new MaintenanceDueResult(MaintenanceDueStatus.Ok, null, null, null, null);
        Assert.Null(MaintenanceCheckService.BuildAlert(Item(), result, Strings));
    }

    [Fact]
    public void BuildAlert_Unknown_ReturnsNull()
    {
        var result = new MaintenanceDueResult(MaintenanceDueStatus.Unknown, null, null, null, null);
        Assert.Null(MaintenanceCheckService.BuildAlert(Item(), result, Strings));
    }

    [Fact]
    public void BuildAlert_DueSoon_ReturnsDueSoonAlert()
    {
        var result = new MaintenanceDueResult(MaintenanceDueStatus.DueSoon, null, null, null, null);
        var alert = MaintenanceCheckService.BuildAlert(Item(id: 7, name: "Tyre rotation"), result, Strings);

        Assert.NotNull(alert);
        Assert.Equal("maintenance-due-soon-7", alert.Value.Category);
        Assert.Contains("Tyre rotation", alert.Value.Body);
    }

    [Fact]
    public void BuildAlert_Overdue_ReturnsOverdueAlert()
    {
        var result = new MaintenanceDueResult(MaintenanceDueStatus.Overdue, null, null, null, null);
        var alert = MaintenanceCheckService.BuildAlert(Item(id: 7, name: "Tyre rotation"), result, Strings);

        Assert.NotNull(alert);
        Assert.Equal("maintenance-overdue-7", alert.Value.Category);
        Assert.Contains("Tyre rotation", alert.Value.Body);
    }

    [Fact]
    public void BuildAlert_CategoryIncludesItemId_TwoItemsDoNotCollide()
    {
        var result = new MaintenanceDueResult(MaintenanceDueStatus.Overdue, null, null, null, null);
        var alertA = MaintenanceCheckService.BuildAlert(Item(id: 1, name: "Oil change"), result, Strings);
        var alertB = MaintenanceCheckService.BuildAlert(Item(id: 2, name: "Tyre rotation"), result, Strings);

        Assert.NotEqual(alertA!.Value.Category, alertB!.Value.Category);
    }
}
