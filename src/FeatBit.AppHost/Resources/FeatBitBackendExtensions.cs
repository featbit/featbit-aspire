using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public static class FeatBitBackendExtensions
{
    public static IResourceBuilder<ContainerResource> AddFeatBitBackend(
        this IDistributedApplicationBuilder builder,
        string name,
        string image,
        int port,
        string telemetryServiceName,
        FeatBitService service,
        FeatBitServiceOptions serviceOptions,
        FeatBitServiceConfigurationResources serviceConfiguration,
        FeatBitOptions options,
        FeatBitPostgresResources postgres,
        FeatBitRedisResources redis,
        FeatBitOpenTelemetryResources telemetry)
    {
        var resource = builder.AddContainer(name, image, options.Version)
            .WithEnvironment("DbProvider", "Postgres")
            .WithEnvironment("Postgres__ConnectionString", postgres.ConnectionString)
            .WithEnvironment("MqProvider", options.UseRedis ? "Redis" : "Postgres")
            .WithEnvironment("CacheProvider", options.UseRedis ? "Redis" : "None")
            .WithHttpEndpoint(
                port: options.IsPublishMode ? null : port,
                targetPort: port,
                name: "http")
            .WithExternalHttpEndpoints()
            .WithHttpHealthCheck("/health/readiness")
            .WithFeatBitOpenTelemetry(telemetryServiceName, options, telemetry)
            .WithFeatBitServiceConfiguration(
                service,
                serviceOptions,
                serviceConfiguration,
                options.Version);

        if (redis.ConnectionString is not null)
        {
            resource.WithEnvironment("Redis__ConnectionString", redis.ConnectionString);
        }

        return resource;
    }
}
