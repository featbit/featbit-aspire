using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace FeatBit.AppHost;

public sealed record FeatBitPostgresResources(
    ReferenceExpression ConnectionString);

public static class FeatBitPostgresExtensions
{
    public static FeatBitPostgresResources AddFeatBitPostgres(
        this IDistributedApplicationBuilder builder)
    {
        NormalizeParameterConfiguration(builder.Configuration);
        var databaseName = builder.Configuration["Parameters:postgres-database"];
        if (databaseName is not null && string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                "Parameters:postgres-database must specify a non-empty PostgreSQL database name.");
        }

        var host = builder.AddParameter("postgres-host")
            .WithDescription("External PostgreSQL server host name.");
        var port = builder.AddParameter("postgres-port")
            .WithDescription("PostgreSQL server port.");
        var user = builder.AddParameter("postgres-user")
            .WithDescription("PostgreSQL login user.");
        var password = builder.AddParameter("postgres-password", secret: true)
            .WithDescription("PostgreSQL login password.");
        var databaseNameParameter = builder.AddParameter("postgres-database")
            .WithDescription("Name of the initialized FeatBit PostgreSQL database.");
        var connection = builder.AddConnectionString(
            "postgres-connection",
            ReferenceExpression.Create(
                $"Host={host};Port={port};Username={user};Password={password};Database={databaseNameParameter}"));

        return new FeatBitPostgresResources(connection.Resource.ConnectionStringExpression);
    }

    private static void NormalizeParameterConfiguration(ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>();
        foreach (var name in new[] { "host", "port", "user", "password", "database" })
        {
            var key = $"Parameters:postgres-{name}";
            var value = GetConfiguredParameter(configuration, key);
            if (value is not null)
            {
                values[key] = value;
            }
        }

        configuration.AddInMemoryCollection(values);
    }

    private static string? GetConfiguredParameter(IConfigurationRoot configuration, string key)
    {
        // Resolve both spellings by provider priority so an environment override
        // is not hidden by the hyphenated key in appsettings.json.
        foreach (var provider in configuration.Providers.Reverse())
        {
            if (provider.TryGet(key, out var value) && value is not null)
            {
                return value;
            }

            if (provider.TryGet(key.Replace('-', '_'), out value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
