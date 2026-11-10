using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

public class DawarichEndpointsTests
{
    [Fact]
    public void EmptySetDisablesFusion() => Assert.Null(DawarichEndpoints.Validate(new([])));

    [Theory]
    [InlineData("")]
    [InlineData(" phone")]
    [InlineData("phone\n")]
    public void InvalidTrackerIsRefused(string tracker) =>
        Assert.NotNull(DawarichEndpoints.Validate(new([tracker])));

    [Fact]
    public void NullDuplicateAndOversizedSetsAreRefused()
    {
        Assert.NotNull(DawarichEndpoints.Validate(new(null)));
        Assert.NotNull(DawarichEndpoints.Validate(new(["a", "a"])));
        Assert.NotNull(DawarichEndpoints.Validate(new([new string('a', 201)])));
        Assert.NotNull(DawarichEndpoints.Validate(new(Enumerable.Range(0, 11).Select(i => $"phone-{i}").ToArray())));
    }
}
