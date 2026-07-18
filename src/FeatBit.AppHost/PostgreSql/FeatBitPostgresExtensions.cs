using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitPostgresResources(
    ReferenceExpression ConnectionString,
    IResourceBuilder<PostgresDatabaseResource>? LocalDatabase);

public static class FeatBitPostgresExtensions
{
    public static async Task<FeatBitPostgresResources> AddFeatBitPostgresAsync(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (options.IsPublishMode)
        {
            var host = builder.AddParameter("postgres-host")
                .WithDescription("PostgreSQL server host name used by the Azure deployment.");
            var port = builder.AddParameter("postgres-port", "5432", publishValueAsDefault: true)
                .WithDescription("PostgreSQL server port.");
            var user = builder.AddParameter("postgres-user")
                .WithDescription("PostgreSQL login user.");
            var password = builder.AddParameter("postgres-password", secret: true)
                .WithDescription("PostgreSQL login password.");
            var databaseNameParameter = builder.AddParameter("postgres-database")
                .WithDescription("Name of the initialized FeatBit PostgreSQL database.");
            var connection = builder.AddConnectionString(
                "postgres",
                ReferenceExpression.Create(
                    $"Host={host};Port={port};Username={user};Password={password};Database={databaseNameParameter}"));

            return new FeatBitPostgresResources(
                connection.Resource.ConnectionStringExpression,
                null);
        }

        var initFiles = await PostgresInitFilesProvider.GetAsync(
            builder.AppHostDirectory,
            options.Version);
        var databaseName = builder.Configuration["Parameters:postgres-database"];
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                "Parameters:postgres-database must specify the local FeatBit PostgreSQL database name.");
        }

        var postgres = builder.AddPostgres("postgres")
            .WithImageTag("15.10")
            .WithDataVolume("featbit-postgres-data")
            .WithInitFiles(initFiles)
            .WithLifetime(ContainerLifetime.Persistent);
        var database = postgres.AddDatabase("featbit-db", databaseName);

        return new FeatBitPostgresResources(
            database.Resource.ConnectionStringExpression,
            database);
    }
}
