using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace FeatBit.AppHost;

public sealed record FeatBitPostgresResources(
    ReferenceExpression ConnectionString)
{
    public IResourceBuilder<PostgresDatabaseResource>? Database { get; init; }
}

public static class FeatBitPostgresExtensions
{
    public static FeatBitPostgresResources AddFeatBitPostgres(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (!options.IsPublishMode && options.UseLocalInfrastructure)
        {
            var initDirectory = Path.Combine(
                builder.AppHostDirectory,
                "infra", "postgresql", options.Version, "docker-entrypoint-initdb.d");
            if (!Directory.Exists(initDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Local PostgreSQL initialization scripts for FeatBit {options.Version} " +
                    $"were not found at '{initDirectory}'.");
            }

            // Use separate local credentials and the database name created by the
            // upstream scripts, regardless of configured external PostgreSQL values.
#pragma warning disable ASPIRECERTIFICATES001 // Local development uses plain PostgreSQL TCP.
            var postgres = builder.AddPostgres("local-postgres")
                .WithImageTag("15.10")
                .WithoutHttpsCertificate()
                .WithDataVolume()
                .WithInitFiles(initDirectory)
                .WithLifetime(ContainerLifetime.Persistent);
#pragma warning restore ASPIRECERTIFICATES001
            var database = postgres.AddDatabase("featbit-db", databaseName: "featbit");
            return new FeatBitPostgresResources(database.Resource.ConnectionStringExpression)
            {
                Database = database
            };
        }

        NormalizeParameterConfiguration(builder.Configuration, options.IsPublishMode);
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

    private static void NormalizeParameterConfiguration(ConfigurationManager configuration, bool isPublishMode)
    {
        var values = new Dictionary<string, string?>();
        foreach (var name in new[] { "host", "port", "user", "password", "database" })
        {
            var key = $"Parameters:postgres-{name}";
            var value = GetConfiguredParameter(configuration, key, isPublishMode);
            if (value is not null)
            {
                values[key] = value;
            }
        }

        configuration.AddInMemoryCollection(values);
    }

    private static string? GetConfiguredParameter(IConfigurationRoot configuration, string key, bool isPublishMode)
    {
        // Resolve both spellings by provider priority so an environment override
        // is not hidden by the hyphenated key in appsettings.json.
        // Aspire 13.6 appends cached deployment inputs through AddJsonStream.
        // Keep them as fallbacks so current files and environment variables win.
        var providers = configuration.Providers.Reverse();
        if (isPublishMode)
        {
            providers = providers.OrderBy(provider => provider is JsonStreamConfigurationProvider);
        }

        foreach (var provider in providers)
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
