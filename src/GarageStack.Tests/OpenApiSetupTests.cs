using System.Text.Json.Nodes;
using GarageStack.Api.Reference;
using Microsoft.OpenApi;

namespace GarageStack.Tests;

/// <summary>
/// The badges the API reference shows beside an endpoint's name: the token badge OpenApiSetup adds
/// sits beside those the endpoint declared itself, which Scalar's transformer has written first.
/// </summary>
public class OpenApiSetupTests
{
    [Fact]
    public void AddBadge_ToAnOperationWithoutBadges_StartsTheList()
    {
        var operation = new OpenApiOperation();

        OpenApiSetup.AddBadge(operation, ApiReferenceText.TokenBadge);

        Assert.Equal([ApiReferenceText.TokenBadge], BadgeNames(operation));
    }

    [Fact]
    public void AddBadge_AfterTheEndpointsOwnBadges_KeepsThemAndComesLast()
    {
        var operation = new OpenApiOperation
        {
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                [OpenApiSetup.BadgesExtension] = new JsonNodeExtension(new JsonArray(new JsonObject { ["name"] = ApiReferenceText.DemoOnlyBadge })),
            },
        };

        OpenApiSetup.AddBadge(operation, ApiReferenceText.TokenBadge);

        Assert.Equal([ApiReferenceText.DemoOnlyBadge, ApiReferenceText.TokenBadge], BadgeNames(operation));
    }

    private static List<string> BadgeNames(OpenApiOperation operation) =>
        [.. ((JsonArray)((JsonNodeExtension)operation.Extensions![OpenApiSetup.BadgesExtension]).Node).Select(b => b!["name"]!.GetValue<string>())];
}
