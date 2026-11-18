using GarageStack.Core.Models;

namespace GarageStack.Tests;

// The buttons a notification offers, by its category. The service worker knows each by name.
public class NotificationActionsTests
{
    [Fact]
    public void UnlockedParked_CanBeLocked() =>
        Assert.Equal(["lock"], NotificationActions.For(NotificationCategories.UnlockedParked));

    [Fact]
    public void MaintenanceReminders_CanBeMarkedDone()
    {
        Assert.Equal(["maintenance-done"], NotificationActions.For(NotificationCategories.MaintenanceOverdue(4)));
        Assert.Equal(["maintenance-done"], NotificationActions.For(NotificationCategories.MaintenanceDueSoon(4)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("doors-open-parked")]
    [InlineData("charging-complete")]
    [InlineData("rdw-apk-due-30-20261108")]
    public void OtherCategories_OfferNothing(string? category) =>
        Assert.Empty(NotificationActions.For(category));

    [Theory]
    [InlineData("maintenance-overdue-7", 7)]
    [InlineData("maintenance-due-soon-123", 123)]
    [InlineData("maintenance-overdue-", null)]
    [InlineData("maintenance-overdue-x", null)]
    [InlineData("maintenance-overdue--3", null)]
    [InlineData("maintenance", null)]
    [InlineData("low-tyre", null)]
    [InlineData(null, null)]
    public void MaintenanceItemId_ReadsTheItemFromItsCategory(string? category, int? id) =>
        Assert.Equal(id, NotificationCategories.MaintenanceItemId(category));
}
