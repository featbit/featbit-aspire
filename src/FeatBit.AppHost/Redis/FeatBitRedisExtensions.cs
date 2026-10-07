using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitRedisResources(
    ReferenceExpression? ConnectionString)
{
    public IResourceBuilder<RedisResource>? Container { get; init; }
}

public static class FeatBitRedisExtensions
{
    public static FeatBitRedisResources AddFeatBitRedis(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (!options.UseRedis)
        {
            return new FeatBitRedisResources(null);
        }

        if (!options.IsPublishMode && options.UseLocalInfrastructure)
        {
#pragma warning disable ASPIRECERTIFICATES001 // Local development uses plain Redis TCP.
            var redis = builder.AddRedis("local-redis")
                .WithoutHttpsCertificate()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent);
#pragma warning restore ASPIRECERTIFICATES001
            return new FeatBitRedisResources(redis.Resource.ConnectionStringExpression)
            {
                Container = redis
            };
        }

        var connectionString = builder.AddConnectionString("redis");
        // Keep the existing secret parameter and expose the external connection
        // as a resource in the dashboard, matching the PostgreSQL connection.
        var connection = builder.AddConnectionString(
            "redis-connection",
            connectionString.Resource.ConnectionStringExpression);
        return new FeatBitRedisResources(connection.Resource.ConnectionStringExpression);
    }
}
