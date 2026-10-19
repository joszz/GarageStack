using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

// A plate is typed the way it is written on the car; the RDW keys it without the separators.
public class LicensePlateTests
{
    [Theory]
    [InlineData("ab-123-c", "AB123C")]
    [InlineData("AB 123 C", "AB123C")]
    [InlineData(" hpk-98-b ", "HPK98B")]
    [InlineData("1-ABC-23", "1ABC23")]
    [InlineData("AB123C", "AB123C")]
    public void Normalize_UpperCasesAndDropsDashesAndSpaces(string typed, string expected)
    {
        Assert.Equal(expected, LicensePlate.Normalize(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("- -")]
    public void Normalize_NothingLeft_IsNull(string? typed)
    {
        Assert.Null(LicensePlate.Normalize(typed));
    }

    [Theory]
    [InlineData("AB123C")]
    [InlineData("1ABC23")]
    [InlineData("ABCDEFGH")]
    public void IsValid_LettersAndDigits_UpToEight(string plate)
    {
        Assert.True(LicensePlate.IsValid(plate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEFGHI")]
    [InlineData("AB.123C")]
    [InlineData("AB'123")]
    [InlineData("ÄB123C")]
    public void IsValid_AnythingElse_IsRefused(string? plate)
    {
        Assert.False(LicensePlate.IsValid(plate));
    }
}
