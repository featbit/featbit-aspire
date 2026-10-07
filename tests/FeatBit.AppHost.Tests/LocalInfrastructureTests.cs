using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using FeatBit.AppHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FeatBit.AppHost.Tests;

public sealed class LocalInfrastructureTests
{
    [Fact]
    public void LocalRunDefaultsToDockerPostgresAndRedisWithoutExternalConfiguration()
    {
        var builder = CreateBuilder();
        var options = FeatBitOptions.Load(builder);
        var postgres = builder.AddFeatBitPostgres(options);
        var redis = builder.AddFeatBitRedis(options);

        Assert.True(options.UseLocalInfrastructure);
        Assert.True(options.UseRedis);
        Assert.Equal("featbit", postgres.Database!.Resource.DatabaseName);
        Assert.Equal("local-postgres", postgres.Database.Resource.Parent.Name);
        Assert.Equal("local-redis", redis.Container!.Resource.Name);
        Assert.DoesNotContain(builder.Resources, resource =>
            resource.Name is "postgres-host" or "postgres-user" or "postgres-password" or "redis");
    }

    [Fact]
    public void LocalInfrastructureIgnoresExistingExternalDatabaseAndRedisSettings()
    {
        var builder = CreateBuilder(new()
        {
            ["Parameters:postgres-host"] = "production.example.com",
            ["Parameters:postgres-user"] = "production-user",
            ["Parameters:postgres-password"] = "external-test-password",
            ["Parameters:postgres-database"] = " ",
            ["ConnectionStrings:redis"] = "production.example.com:6379"
        });
        var options = FeatBitOptions.Load(builder);
        var postgres = builder.AddFeatBitPostgres(options);
        var redis = builder.AddFeatBitRedis(options);

        Assert.Equal("featbit", postgres.Database!.Resource.DatabaseName);
        Assert.Equal("local-postgres-password", postgres.Database.Resource.Parent.PasswordParameter.Name);
        Assert.NotNull(redis.Container);
        Assert.DoesNotContain(builder.Resources, resource => resource is ConnectionStringResource);
    }

    [Theory]
    [InlineData(FeatBitService.Api, "featbit-api", 5000)]
    [InlineData(FeatBitService.Els, "featbit-evaluation", 5100)]
    public void BothBackendsWaitForLocalInfrastructure(
        FeatBitService service, string name, int port)
    {
        var builder = CreateBuilder(new() { ["FeatBit:OpenTelemetry:Enabled"] = "false" });
        var options = FeatBitOptions.Load(builder);
        var postgres = builder.AddFeatBitPostgres(options);
        var redis = builder.AddFeatBitRedis(options);
        var backend = builder.AddFeatBitBackend(
            name, "test-image", port, name, service,
            service == FeatBitService.Api ? options.Api : options.Els,
            builder.AddFeatBitServiceConfiguration(options), options, postgres, redis,
            builder.AddFeatBitOpenTelemetry(options));

        var waits = backend.Resource.Annotations.OfType<WaitAnnotation>();
        Assert.Contains(waits, wait => ReferenceEquals(wait.Resource, postgres.Database!.Resource));
        Assert.Contains(waits, wait => ReferenceEquals(wait.Resource, redis.Container!.Resource));
    }

    [Fact]
    public void StandaloneLocalModeDoesNotStartRedis()
    {
        var builder = CreateBuilder(new() { ["FeatBit:UseRedis"] = "false" });
        var options = FeatBitOptions.Load(builder);
        var postgres = builder.AddFeatBitPostgres(options);
        var redis = builder.AddFeatBitRedis(options);

        Assert.NotNull(postgres.Database);
        Assert.Null(redis.ConnectionString);
        Assert.DoesNotContain(builder.Resources, resource => resource is RedisResource);
    }

    [Fact]
    public void ExternalServicesCanBeSelectedExplicitlyForLocalRuns()
    {
        var builder = CreateBuilder(new()
        {
            ["FeatBit:UseLocalInfrastructure"] = "false",
            ["FeatBit:UseRedis"] = "true"
        });
        var options = FeatBitOptions.Load(builder);
        var postgres = builder.AddFeatBitPostgres(options);
        var redis = builder.AddFeatBitRedis(options);

        Assert.False(options.UseLocalInfrastructure);
        Assert.Null(postgres.Database);
        Assert.Null(redis.Container);
        Assert.Contains(builder.Resources, resource => resource.Name == "postgres-connection");
        Assert.Contains(builder.Resources, resource => resource.Name == "redis-connection");
        Assert.DoesNotContain(builder.Resources, resource => resource is ContainerResource);
    }

    [Fact]
    public void PublishAlwaysUsesExternalServicesEvenIfLocalInfrastructureIsConfigured()
    {
        var builder = CreateBuilder(new()
        {
            ["FeatBit:UseLocalInfrastructure"] = "true",
            ["FeatBit:UseRedis"] = "true"
        }, ["--operation", "publish"]);
        var options = FeatBitOptions.Load(builder);
        var postgres = builder.AddFeatBitPostgres(options);
        var redis = builder.AddFeatBitRedis(options);

        Assert.True(options.IsPublishMode);
        Assert.False(options.UseLocalInfrastructure);
        Assert.Null(postgres.Database);
        Assert.Null(redis.Container);
        Assert.DoesNotContain(builder.Resources, resource => resource is ContainerResource);
    }

    [Fact]
    public void MissingVersionedSchemaFailsBeforeAddingLocalDatabaseResources()
    {
        var builder = CreateBuilder();
        var options = FeatBitOptions.Load(builder) with { Version = "0.0.0-missing" };

        var error = Assert.Throws<DirectoryNotFoundException>(() => builder.AddFeatBitPostgres(options));
        Assert.Contains(options.Version, error.Message);
        Assert.DoesNotContain(builder.Resources, resource => resource is PostgresServerResource);
    }

    private static IDistributedApplicationBuilder CreateBuilder(
        Dictionary<string, string?>? configuration = null, string[]? args = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "apphost.csproj")))
        {
            directory = directory.Parent;
        }

        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = args ?? [],
            AssemblyName = typeof(FeatBitOptions).Assembly.GetName().Name,
            ProjectDirectory = directory!.FullName,
            DisableDashboard = true
        });
        var appHostConfiguration = builder.Configuration.GetSection("AppHost")
            .AsEnumerable().ToDictionary(entry => entry.Key, entry => entry.Value);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(appHostConfiguration);
        builder.Configuration.AddInMemoryCollection(configuration ?? new());
        return builder;
    }
}
