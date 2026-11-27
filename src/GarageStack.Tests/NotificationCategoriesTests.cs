using GarageStack.Core.Models;

namespace GarageStack.Tests;

// Each device switches notification types off for itself, and the Worker leaves that device out
// of the push. These are the rules it decides by, matching the frontend's notificationCategoryId.
public class NotificationCategoriesTests
{
    [Theory]
    [InlineData("low-tyre", "low-tyre")]
    [InlineData("low-12v", "low-12v")]
    [InlineData("gateway-login", "gateway-login")]
    [InlineData("maintenance-overdue-7", "maintenance")]
    [InlineData("maintenance-due-soon-12", "maintenance")]
    [InlineData("rdw-recall-MGP230061", "rdw")]
    [InlineData("rdw-apk-due-30-20261108", "rdw")]
    [InlineData("rdw-apk-expired-20261108", "rdw")]
    [InlineData("rdw-uninsured", "rdw")]
    public void GroupOf_KnownCategory_IsTheTypeTheChecklistOffers(string category, string group)
    {
        Assert.Equal(group, NotificationCategories.GroupOf(category));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-new")]
    public void GroupOf_CategoryWithoutAType_IsNull(string? category)
    {
        Assert.Null(NotificationCategories.GroupOf(category));
    }

    [Fact]
    public void IsWanted_TypeSwitchedOff_IsLeftOut()
    {
        Assert.False(NotificationCategories.IsWanted("engine-start", ["engine-start"]));
    }

    [Fact]
    public void IsWanted_MaintenanceSwitchedOff_LeavesOutEveryItemsAlert()
    {
        Assert.False(NotificationCategories.IsWanted("maintenance-overdue-3", ["maintenance"]));
    }

    [Fact]
    public void IsWanted_OtherTypeSwitchedOff_StillGoesOut()
    {
        Assert.True(NotificationCategories.IsWanted("low-tyre", ["engine-start"]));
    }

    // The checklist cannot switch such a notification off, so it must never be lost.
    [Fact]
    public void IsWanted_NotificationWithoutAType_AlwaysGoesOut()
    {
        Assert.True(NotificationCategories.IsWanted(null, ["engine-start"]));
    }

    // A type switched off for one car leaves out that car's notifications of it, and no other car's.
    [Fact]
    public void IsWanted_TypeSwitchedOffForOneCar_IsLeftOutForThatCarOnly()
    {
        IReadOnlyCollection<string> offForThisCar = ["doors-open-parked"];

        Assert.False(NotificationCategories.IsWanted("doors-open-parked", [], null, offForThisCar));
        Assert.True(NotificationCategories.IsWanted("doors-open-parked", [], null, null));
        Assert.True(NotificationCategories.IsWanted("doors-open-parked", [], null, ["low-tyre"]));
        Assert.True(NotificationCategories.IsWanted("low-tyre", [], null, offForThisCar));
    }

    // Either list keeps a type out: the device's own does not come back for a car that has it on.
    [Fact]
    public void IsWanted_TypeSwitchedOffForEveryCar_StaysOutWhateverTheCarSays()
    {
        Assert.False(NotificationCategories.IsWanted("engine-start", ["engine-start"], null, []));
        Assert.False(NotificationCategories.IsWanted("engine-start", ["engine-start"], null, ["engine-start"]));
    }

    // The per-car list names the type the checklist offers, so a car's maintenance and RDW types
    // switch off as a group, as they do for the whole device.
    [Fact]
    public void IsWanted_GroupedTypeSwitchedOffForOneCar_LeavesOutEveryOfItsCategories()
    {
        Assert.False(NotificationCategories.IsWanted("maintenance-overdue-3", [], null, ["maintenance"]));
        Assert.False(NotificationCategories.IsWanted("rdw-recall-MGP230061", [], null, ["rdw"]));
    }

    // A summary is switched on for the device and can still be switched off for one car.
    [Fact]
    public void IsWanted_OptInTypeSwitchedOffForOneCar_IsLeftOutForThatCar()
    {
        Assert.False(NotificationCategories.IsWanted("weekly-summary", [], ["weekly-summary"], ["weekly-summary"]));
        Assert.True(NotificationCategories.IsWanted("weekly-summary", [], ["weekly-summary"], ["low-tyre"]));
        Assert.False(NotificationCategories.IsWanted("weekly-summary", [], null, []));
    }

    // A notification without a type, or without a car, answers to what the checklist can switch off.
    [Fact]
    public void IsWanted_NotificationWithoutATypeOrACar_IgnoresThePerCarList()
    {
        Assert.True(NotificationCategories.IsWanted(null, [], null, ["gateway-login", "engine-start"]));
        Assert.True(NotificationCategories.IsWanted("gateway-login", [], null, null));
    }

    [Fact]
    public void ExcludedCategoriesFor_ReturnsTheTypesOffForThatCarAndNullForOthers()
    {
        var subscription = new PushSubscription { ExcludedCategoriesByVehicle = { [7] = ["low-tyre"] } };

        Assert.Equal(["low-tyre"], subscription.ExcludedCategoriesFor(7));
        Assert.Null(subscription.ExcludedCategoriesFor(8));
        Assert.Null(subscription.ExcludedCategoriesFor(null));
    }

    // A summary every week unasked would be noise, so it goes out only to a device that asked.
    [Fact]
    public void IsWanted_OptInType_GoesOutOnlyOnceSwitchedOn()
    {
        Assert.False(NotificationCategories.IsWanted("weekly-summary", []));
        Assert.False(NotificationCategories.IsWanted("weekly-summary", [], ["monthly-summary"]));
        Assert.True(NotificationCategories.IsWanted("weekly-summary", [], ["weekly-summary"]));
        Assert.True(NotificationCategories.IsWanted("low-tyre", [], ["weekly-summary"]));
    }
}
