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
}
