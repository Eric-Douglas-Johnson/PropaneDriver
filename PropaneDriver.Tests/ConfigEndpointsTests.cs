using Microsoft.Extensions.Configuration;

namespace PropaneDriver.Tests;

// GET /api/config/maps-key resolves the browser-facing Maps key from either
// the "GoogleMaps:JsApiKey" config section or the GOOGLE_MAPS_JS_API_KEY
// environment variable. These tests exercise that resolution logic.
public class ConfigEndpointsTests
{
    private const string EnvVarName = "GOOGLE_MAPS_JS_API_KEY";

    private static string? ResolveKey(IConfiguration config)
        => config["GoogleMaps:JsApiKey"]
           ?? Environment.GetEnvironmentVariable(EnvVarName);

    [Fact]
    public void MapsKey_FromConfigSection_Wins()
    {
        var originalEnv = Environment.GetEnvironmentVariable(EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(EnvVarName, "env-key");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GoogleMaps:JsApiKey"] = "config-key"
                })
                .Build();

            Assert.Equal("config-key", ResolveKey(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, originalEnv);
        }
    }

    [Fact]
    public void MapsKey_FallsBackToEnvironment_WhenConfigMissing()
    {
        var originalEnv = Environment.GetEnvironmentVariable(EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(EnvVarName, "env-key");
            var config = new ConfigurationBuilder().Build();

            Assert.Equal("env-key", ResolveKey(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, originalEnv);
        }
    }

    [Fact]
    public void MapsKey_BothMissing_ReturnsNullOrWhitespace()
    {
        var originalEnv = Environment.GetEnvironmentVariable(EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
            var config = new ConfigurationBuilder().Build();

            Assert.True(string.IsNullOrWhiteSpace(ResolveKey(config)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, originalEnv);
        }
    }
}
