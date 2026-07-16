using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitRedisResources(
    ReferenceExpression? ConnectionString,
    IResourceBuilder<RedisResource>? LocalResource);

public static class FeatBitRedisExtensions
{
    public static FeatBitRedisResources AddFeatBitRedis(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (!options.UseRedis)
        {
            return new FeatBitRedisResources(null, null);
        }

        if (options.IsPublishMode)
        {
            var connection = builder.AddConnectionString("redis");
            return new FeatBitRedisResources(
                connection.Resource.ConnectionStringExpression,
                null);
        }

        // Redis is derived cache data. Keeping it session-scoped avoids reusing
        // FeatBit's persistent `redis-is-populated` marker after Redis is toggled.
        var redis = builder.AddRedis("redis");
        return new FeatBitRedisResources(
            redis.Resource.ConnectionStringExpression,
            redis);
    }
}
