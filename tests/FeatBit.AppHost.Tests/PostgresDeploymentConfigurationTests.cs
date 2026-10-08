using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using FeatBit.AppHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FeatBit.AppHost.Tests;

public sealed class PostgresDeploymentConfigurationTests
{
    private static readonly Dictionary<string, string?> CurrentSettings = new()
    {
        ["Parameters:postgres-host"] = "new.example.com",
        ["Parameters:postgres-port"] = "5433",
        ["Parameters:postgres-user"] = "new-user",
        ["Parameters:postgres-database"] = "new-database"
    };

    private static readonly Dictionary<string, string?> CachedSettings = new()
    {
        ["Parameters:postgres-host"] = "old.example.com",
        ["Parameters:postgres-port"] = "5432",
        ["Parameters:postgres-user"] = "old-user",
        ["Parameters:postgres-database"] = "old-database",
        ["Parameters:postgres-password"] = "cached-test-password",
        ["Parameters:jwt-key"] = "cached-test-jwt"
    };

    [Fact]
    public async Task CurrentDatabaseSettingsReplaceCachedDeploymentInputs()
    {
        var builder = CreateBuilder(CurrentSettings);
        // Match Aspire's LoadDeploymentState: append the flattened cached inputs.
        builder.Configuration.AddJsonStream(ToStream(CachedSettings));
        Assert.Equal("old.example.com", builder.Configuration["Parameters:postgres-host"]);

        var postgres = builder.AddFeatBitPostgres(FeatBitOptions.Load(builder));
        var connection = await postgres.ConnectionString.GetValueAsync(default);

        Assert.Equal(
            "Host=new.example.com;Port=5433;Username=new-user;Password=cached-test-password;Database=new-database",
            connection);
        Assert.Equal("cached-test-jwt", builder.Configuration["Parameters:jwt-key"]);
        Assert.True(builder.Resources.OfType<ParameterResource>().Single(p => p.Name == "postgres-password").Secret);
    }

    [Theory]
    [InlineData("postgres-host", "environment.example.com")]
    [InlineData("postgres-port", "6432")]
    [InlineData("postgres-user", "environment-user")]
    [InlineData("postgres-database", "environment-database")]
    [InlineData("postgres-password", "current-test-password")]
    public async Task CurrentUnderscoreOverridesWinOverFilesAndCachedValues(string parameter, string value)
    {
        var builder = CreateBuilder(CurrentSettings);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Parameters:{parameter.Replace('-', '_')}"] = value
        });
        builder.Configuration.AddJsonStream(ToStream(CachedSettings));

        builder.AddFeatBitPostgres(FeatBitOptions.Load(builder));
        var resource = builder.Resources.OfType<ParameterResource>().Single(p => p.Name == parameter);

        Assert.Equal(value, await resource.GetValueAsync(default));
    }

    [Fact]
    public async Task CachedDatabaseInputsRemainAvailableWhenNoCurrentValuesExist()
    {
        var builder = CreateBuilder(new());
        builder.Configuration.AddJsonStream(ToStream(CachedSettings));

        var postgres = builder.AddFeatBitPostgres(FeatBitOptions.Load(builder));

        Assert.Equal(
            "Host=old.example.com;Port=5432;Username=old-user;Password=cached-test-password;Database=old-database",
            await postgres.ConnectionString.GetValueAsync(default));
    }

    [Fact]
    public void CurrentEmptyDatabaseNameFailsInsteadOfReusingTheCachedDatabase()
    {
        var builder = CreateBuilder(new() { ["Parameters:postgres-database"] = " " });
        builder.Configuration.AddJsonStream(ToStream(CachedSettings));

        var error = Assert.Throws<InvalidOperationException>(
            () => builder.AddFeatBitPostgres(FeatBitOptions.Load(builder)));

        Assert.Contains("non-empty PostgreSQL database name", error.Message);
    }

    private static MemoryStream ToStream(Dictionary<string, string?> values) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));

    private static IDistributedApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "apphost.csproj")))
        {
            directory = directory.Parent;
        }

        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = ["--operation", "publish"],
            AssemblyName = typeof(FeatBitOptions).Assembly.GetName().Name,
            ProjectDirectory = directory!.FullName,
            DisableDashboard = true
        });
        var appHostConfiguration = builder.Configuration.GetSection("AppHost")
            .AsEnumerable().ToDictionary(entry => entry.Key, entry => entry.Value);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(appHostConfiguration);
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
