namespace FeatBit.AppHost;

public static class AppHostSrvWebApi
{
    private const int Port = 5000;

    public static IResourceBuilder<ContainerResource> AddWebApiService(
        this IDistributedApplicationBuilder builder,
        string dbProvider,
        string databaseConnectionString,
        string redisConnectionString,
        IResourceBuilder<ContainerResource> dataAnalytics,
        IResourceBuilder<IResourceWithConnectionString>? applicationInsights = null)
    {
        var isPublishMode = builder.ExecutionContext.IsPublishMode;
        var isMongoDb = dbProvider.Equals("MongoDb", StringComparison.OrdinalIgnoreCase);
        var dbEnvKey = isMongoDb ? "MongoDb__ConnectionString" : "Postgres__ConnectionString";

        var container = builder
            .AddContainer("featbit-api", "featbit/featbit-api-server", "latest")
            .WithEnvironment("DbProvider", dbProvider)
            .WithEnvironment(dbEnvKey, databaseConnectionString)
            .WithEnvironment("Redis__ConnectionString", redisConnectionString)
            .WithEnvironment("SSOEnabled", "true")
            .WithEnvironment("MqProvider", "Redis")
            .WithEnvironment("CacheProvider", "Redis")
            .WithEnvironment("OLAP__ServiceHost", dataAnalytics.GetEndpoint("http"))
            .WithEnvironment("ASPNETCORE_URLS", $"http://+:{Port.ToString()}")
            .WithEnvironment("AllowedHosts", "*");

        container = isPublishMode
            ? container.WithHttpEndpoint(targetPort: Port, name: "http")
                       .WithHttpsEndpoint(targetPort: Port, name: "https")
            : container.WithHttpEndpoint(port: Port, targetPort: Port, name: "http");

        // Mark endpoints as external BEFORE PublishAsAzureContainerApp
        container = container.WithExternalHttpEndpoints();

        container = container
            .WithHttpHealthCheck("/health/liveness")
            .PublishAsAzureContainerApp((_, app) =>
            {
                app.Template.Scale.MinReplicas = 1;
                app.Template.Scale.MaxReplicas = 10;
                app.Configuration.Ingress.External = true;
                
                var containerResource = app.Template.Containers[0].Value!;
                containerResource.Resources = new()
                {
                Cpu = 0.75,
                Memory = "1.5Gi"
                };
            });

        if (applicationInsights is not null)
        {
            container = container.WithEnvironment(
                "APPLICATIONINSIGHTS_CONNECTION_STRING",
                applicationInsights);
        }

        return container;
    }
}
