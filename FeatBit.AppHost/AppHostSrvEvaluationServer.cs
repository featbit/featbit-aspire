using Aspire.Hosting;

namespace FeatBit.AppHost;

public static class AppHostSrvEvaluationServer
{
    private const int Port = 5100;

    public static IResourceBuilder<ContainerResource> AddEvaluationServerService(
        this IDistributedApplicationBuilder builder,
        string dbProvider,
        string databaseConnectionString,
        string redisConnectionString,
        IResourceBuilder<IResourceWithConnectionString>? applicationInsights = null)
    {
        var isPublishMode = builder.ExecutionContext.IsPublishMode;

        var dbEnvKey = dbProvider.Equals("MongoDb", StringComparison.OrdinalIgnoreCase) ?
            "MongoDb__ConnectionString" :
            "Postgres__ConnectionString";

        var container = builder
            .AddContainer("featbit-evaluation-server", "featbit/featbit-evaluation-server", "latest")
            .WithEnvironment("DbProvider", dbProvider)
            .WithEnvironment(dbEnvKey, databaseConnectionString)
            .WithEnvironment("Redis__ConnectionString", redisConnectionString)
            .WithEnvironment("SSOEnabled", "true")
            .WithEnvironment("MqProvider", "Redis")
            .WithEnvironment("CacheProvider", "Redis")
            .WithEnvironment("ASPNETCORE_URLS", $"http://+:{Port.ToString()}")
            .WithEnvironment("AllowedHosts", "*")
            .WithEnvironment("Streaming__TrackClientHostName", "false");
        
        // Configure endpoints based on execution context
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

        // Add Application Insights if enabled
        if (applicationInsights is not null)
        {
            container = container.WithEnvironment(
                "APPLICATIONINSIGHTS_CONNECTION_STRING",
                applicationInsights);
        }

        return container;
    }
}
