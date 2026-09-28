using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitRedisResources(
    ReferenceExpression? ConnectionString);

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

        var connectionString = builder.AddConnectionString("redis");
        // Keep the existing secret parameter and expose the external connection
        // as a resource in the dashboard, matching the PostgreSQL connection.
        var connection = builder.AddConnectionString(
            "redis-connection",
            connectionString.Resource.ConnectionStringExpression);
        return new FeatBitRedisResources(connection.Resource.ConnectionStringExpression);
    }
}
