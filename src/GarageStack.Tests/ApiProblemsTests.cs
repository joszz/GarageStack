using GarageStack.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GarageStack.Tests;

public class ApiProblemsTests
{
    [Fact]
    public void BadRequest_CarriesTheCodeBesideTheEnglishDetail()
    {
        var result = Assert.IsType<ProblemHttpResult>(
            ApiProblems.BadRequest(new ValidationError("maintenance.nameRequired", "Name is required")));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("Name is required", result.ProblemDetails.Detail);
        Assert.Equal("maintenance.nameRequired", result.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void Problem_UsesTheStatusItIsGiven()
    {
        var result = Assert.IsType<ProblemHttpResult>(
            ApiProblems.Problem(StatusCodes.Status409Conflict, "vehicle.accountUnknown", "Not known yet"));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("vehicle.accountUnknown", result.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData("", null, 10_000.0, null, "maintenance.nameRequired")]
    [InlineData("Oil", null, null, null, "maintenance.intervalRequired")]
    [InlineData("Oil", null, 0.0, null, "maintenance.intervalKmOutOfRange")]
    [InlineData("Oil", null, null, 121, "maintenance.intervalMonthsOutOfRange")]
    public void MaintenanceValidation_NamesTheRuleThatFailed(
        string name, string? notes, double? intervalKm, int? intervalMonths, string code)
    {
        Assert.Equal(code, MaintenanceEndpoints.ValidateItem(name, notes, intervalKm, intervalMonths)?.Code);
    }

    [Fact]
    public void CommandValidation_NamesTheRuleThatFailed()
    {
        Assert.Equal("command.invalidValue", VehicleEndpoints.ValidateCommandValue("climate", "start")?.Code);
    }
}
