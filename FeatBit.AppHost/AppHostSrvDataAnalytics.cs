using FeatBit.AppHost.Utils;

namespace FeatBit.AppHost;

public static class AppHostSrvDataAnalytics
{
    private const string ServiceName = "featbit-data-analytics";

    public static IResourceBuilder<ContainerResource> AddDataAnalyticsService(
        this IDistributedApplicationBuilder builder,
        string dbProvider,
        string databaseConnectionString,
        IResourceBuilder<IResourceWithConnectionString>? applicationInsights = null)
    {
        var isMongoDb = dbProvider.Equals("MongoDb", StringComparison.OrdinalIgnoreCase);

        var container = builder
            .AddContainer("featbit-da-server", "featbit/featbit-data-analytics-server", "latest")
            .WithHttpEndpoint(port: 8200, targetPort: 80, name: "http", isProxied: false)
            .WithEnvironment("DB_PROVIDER", dbProvider)
            .WithEnvironment("CHECK_DB_LIVNESS", "false")
            .WithEnvironment("LOG_LEVEL", "INFO")
            .WithEnvironment("ENABLE_METRICS", "true")
            .WithEnvironment("METRICS_PORT", "9090")
            .WithEnvironment("HEALTH_CHECK_PATH", "/health")
            .PublishAsAzureContainerApp((_, app) =>
            {
                app.Template.Scale.MinReplicas = 3;
                app.Template.Scale.MaxReplicas = 10;
                app.Configuration.Ingress.External = false;
            });

        if (isMongoDb)
        {
            container = container
                .WithEnvironment("MONGO_URI", databaseConnectionString)
                .WithEnvironment("MONGO_INITDB_DATABASE", builder.Configuration["MongoDb:Database"] ?? "featbit")
                .WithEnvironment("MONGO_HOST", builder.Configuration["MongoDb:Host"] ?? "mongodb");
        }
        else
        {
            // PostgreSQL requires parsing the connection string to extract individual components
            container = ConfigurePostgres(container, databaseConnectionString);
        }

        if (applicationInsights is not null)
        {
            container = container
                .WithEnvironment("OTEL_SERVICE_NAME", ServiceName)
                .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", $"service.name={ServiceName},service.version=1.0.0")
                .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsights);
        }

        return container;
    }

    private static IResourceBuilder<ContainerResource> ConfigurePostgres(
        IResourceBuilder<ContainerResource> container,
        string connectionString)
    {
        var (host, user, password, port) = PostgresConnectionStringParser.ParseConnectionStringWithPort(connectionString);
        return container
            .WithEnvironment("POSTGRES_DATABASE", "featbit")
            .WithEnvironment("POSTGRES_HOST", host)
            .WithEnvironment("POSTGRES_PORT", port)
            .WithEnvironment("POSTGRES_USER", user)
            .WithEnvironment("POSTGRES_PASSWORD", password);
    }
}
