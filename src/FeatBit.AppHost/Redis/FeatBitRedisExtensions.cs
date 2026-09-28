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

        var connection = builder.AddConnectionString("redis");
        return new FeatBitRedisResources(connection.Resource.ConnectionStringExpression);
    }
}
