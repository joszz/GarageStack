using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class TripCounterUseTests
{
    [Fact]
    public void Rise_IsTheCountersGainFromBeforeTheTripToItsEnd()
    {
        // A hybrid's fuel counter, in hundredths of a litre: 1.85 L over the trip.
        Assert.Equal(185, TripCounterUse.Rise([412, 450, 530, 597]));
    }

    [Fact]
    public void Rise_TakesAResetPastMidnightAsNewUse()
    {
        // 40 before midnight, then the counter starts from zero and counts 60 more.
        Assert.Equal(100, TripCounterUse.Rise([900, 940, 0, 25, 60]));
    }

    [Fact]
    public void Rise_CountsADipNeitherWay()
    {
        // A reading out of step dips and comes back: only the rise above the peak counts.
        Assert.Equal(30, TripCounterUse.Rise([500, 520, 515, 520, 530]));
    }

    [Fact]
    public void Rise_FromACounterAtZero_CountsItsRise()
    {
        // The first trip of the day starts from a counter that has just been reset.
        Assert.Equal(48, TripCounterUse.Rise([0, 0, 20, 48]));
    }

    [Theory]
    [InlineData(new double[0])]
    [InlineData(new double[] { 412 })]
    public void Rise_WithoutTwoReadings_IsUnknown(double[] readings)
    {
        Assert.Null(TripCounterUse.Rise(readings));
    }

    [Fact]
    public void Rise_OfACounterThatDidNotMove_IsZero()
    {
        Assert.Equal(0, TripCounterUse.Rise([412, 412, 412]));
    }
}
