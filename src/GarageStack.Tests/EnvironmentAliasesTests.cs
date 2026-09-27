using System.Collections;
using System.Text.RegularExpressions;
using GarageStack.Api.Authentication;
using GarageStack.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Tests;

/// <summary>
/// The deployment files pass the variables users set (MQTT_HOST, OIDC_AUTHORITY, ...) straight
/// through, and <see cref="EnvironmentAliases"/> is what turns them into settings.
/// </summary>
public class EnvironmentAliasesTests
{
    private static IConfiguration Build(IDictionary environment, params (string Key, string? Value)[] defaults) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(defaults.Select(d => new KeyValuePair<string, string?>(d.Key, d.Value)))
            .AddGarageStackEnvironment(environment)
            .Build();

    [Fact]
    public void AVariableThatIsSet_FillsItsSetting()
    {
        var configuration = Build(new Dictionary<string, string?> { ["MQTT_HOST"] = "broker.lan" });

        Assert.Equal("broker.lan", configuration["Mqtt:Host"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankVariable_LeavesTheSettingAtItsDefault(string blank)
    {
        // An empty Unraid field, or compose passing through a variable nobody set.
        var configuration = Build(
            new Dictionary<string, string?> { ["MQTT_HOST"] = blank },
            ("Mqtt:Host", "mosquitto"));

        Assert.Equal("mosquitto", configuration["Mqtt:Host"]);
    }

    [Theory]
    [InlineData("Mqtt__Host")]
    [InlineData("MQTT__HOST")]
    public void TheSettingsOwnKey_WinsOverTheVariable(string ownKey)
    {
        // The all-in-one entrypoint derives the broker address this way: its broker runs inside
        // the container, whatever MQTT_HOST says.
        var values = EnvironmentAliases.Resolve(new Dictionary<string, string?>
        {
            ["MQTT_HOST"] = "broker.lan",
            [ownKey] = "127.0.0.1",
        });

        Assert.False(values.ContainsKey("Mqtt:Host"));
    }

    [Fact]
    public void ABlankOwnKey_DoesNotHoldTheVariableBack()
    {
        var values = EnvironmentAliases.Resolve(new Dictionary<string, string?>
        {
            ["MQTT_HOST"] = "broker.lan",
            ["Mqtt__Host"] = "",
        });

        Assert.Equal("broker.lan", values["Mqtt:Host"]);
    }

    [Fact]
    public void BlankSwitches_BindToTheirDefaults()
    {
        // Binding a blank value into a bool throws, which is why blank variables are dropped
        // rather than passed on.
        var configuration = Build(new Dictionary<string, string?>
        {
            ["OIDC_AUTHORITY"] = "https://auth.example.com",
            ["OIDC_AUTO_LOGIN"] = "",
            ["OIDC_REQUIRE_HTTPS_METADATA"] = "",
            ["OIDC_SCOPES"] = "",
        });

        var oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>()!;

        Assert.Equal("https://auth.example.com", oidc.Authority);
        Assert.False(oidc.AutoLogin);
        Assert.True(oidc.RequireHttpsMetadata);
        Assert.Equal("openid profile email", oidc.Scopes);
    }

    [Fact]
    public void EveryVariable_FillsADifferentSetting()
    {
        var keys = EnvironmentAliases.Map.Values.ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── What the deployment files pass ────────────────────────────────────────

    // Read by the services under exactly these names.
    private static readonly HashSet<string> ReadDirectly =
        ["ASPNETCORE_ENVIRONMENT", "ASPNETCORE_URLS", "DEBUG_LOGS", "DEMO_MODE", "SAIC_USER", "SAIC_PASSWORD"];

    // Consumed by the deployment itself (compose, the entrypoint, the gateway, Mosquitto,
    // Postgres) and never meant to reach .NET.
    private static readonly HashSet<string> DeploymentOnly =
    [
        "APP_VERSION", "API_PORT", "FRONTEND_PORT",
        "SAIC_REGION", "SAIC_REST_URI",
        "POSTGRES_HOST", "POSTGRES_PORT", "POSTGRES_DB", "POSTGRES_USER", "POSTGRES_PASSWORD",
        "MQTT_EXTERNAL_PORT", "MQTT_BIND_ADDRESS", "HA_MQTT_USERNAME", "HA_MQTT_PASSWORD",
    ];

    private static bool ReachesASetting(string variable) =>
        EnvironmentAliases.Map.ContainsKey(variable)
        || variable.Contains("__", StringComparison.Ordinal)
        || ReadDirectly.Contains(variable);

    [Theory]
    [InlineData(".env.example")]
    [InlineData("unraid/garagestack.xml")]
    [InlineData("documentation/CONFIGURATION.md")]
    public void EveryDocumentedVariable_IsOneTheAppOrTheDeploymentReads(string file)
    {
        var unknown = DocumentedVariables(file)
            .Where(v => !ReachesASetting(v) && !DeploymentOnly.Contains(v))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void TheConfigurationReference_ListsEveryVariableTheAppTranslates()
    {
        var documented = DocumentedVariables("documentation/CONFIGURATION.md").ToHashSet();
        var missing = EnvironmentAliases.Map.Keys.Where(v => !documented.Contains(v)).ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.demo.yml")]
    public void EveryVariableComposePassesToDotnet_ReachesASetting(string file)
    {
        var variables = DotnetComposeVariables(file).ToList();
        var unknown = variables.Where(v => !ReachesASetting(v)).ToList();

        Assert.NotEmpty(variables);
        Assert.Empty(unknown);
    }

    /// <summary>
    /// The variables a file documents: Target="NAME" in the Unraid template, names in code spans
    /// in a Markdown page, and set or commented-out assignments ("NAME=value", "# NAME=value") in
    /// .env.example.
    /// </summary>
    private static IEnumerable<string> DocumentedVariables(string file)
    {
        var text = File.ReadAllText(RepositoryFile(file));
        var pattern = Path.GetExtension(file) switch
        {
            ".xml" => @"Target=""([A-Z][A-Z0-9_]*)""",
            // Values such as SSO share the capitals, but every variable name has an underscore.
            ".md" => @"`([A-Z][A-Z0-9]*_[A-Z0-9_]*)`",
            _ => @"(?m)^#?\s*([A-Z][A-Z0-9_]*)=",
        };

        return Regex.Matches(text, pattern).Select(m => m.Groups[1].Value).Distinct();
    }

    /// <summary>
    /// The environment keys compose gives the .NET services: the shared x-dotnet-environment
    /// block, and the environment of the api and worker services.
    /// </summary>
    private static IEnumerable<string> DotnetComposeVariables(string file)
    {
        string? section = null;
        string? service = null;
        var inEnvironment = false;

        foreach (var line in File.ReadLines(RepositoryFile(file)))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var indent = line.Length - trimmed.Length;
            var key = Regex.Match(trimmed, @"^([A-Za-z_][A-Za-z0-9_-]*):").Groups[1].Value;

            if (indent == 0)
            {
                section = key;
                service = null;
                inEnvironment = false;
            }
            else if (section == "x-dotnet-environment" && indent == 2 && key.Length > 0)
                yield return key;
            else if (section == "services" && indent == 2)
            {
                service = key;
                inEnvironment = false;
            }
            else if (section == "services" && indent == 4)
                inEnvironment = key == "environment";
            else if (inEnvironment && indent == 6 && service is "api" or "worker" && key.Length > 0)
                yield return key;
        }
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageStack.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }
}
