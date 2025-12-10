using FeatBit.AppHost.Utils;
using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

// Add Azure Container Apps environment when publishing to Azure
// This enables deployment to Azure Container Apps using 'azd up' command
var containerAppsEnv = builder.AddAzureContainerAppEnvironment("featbit-aspire");

// PostgreSQL Resource - use existing connection string
var postgresConnectionString = builder.Configuration["ConnectionStrings:Postgres"]
    ?? throw new InvalidOperationException("Postgres connection string is required");
var postgres = builder.AddConnectionString("postgres", postgresConnectionString);

// Redis Resource - use existing connection string
var redisConnectionString = builder.Configuration["ConnectionStrings:Redis"]
    ?? throw new InvalidOperationException("Redis connection string is required");
var redis = builder.AddConnectionString("redis", redisConnectionString);

// Application Insights Resource - only use in publish mode (when deploying to Azure)
IResourceBuilder<IResourceWithConnectionString>? applicationInsights = null;
if (builder.ExecutionContext.IsPublishMode)
{
    // Azure Application Insights - for comprehensive monitoring and observability
    applicationInsights = builder.AddAzureApplicationInsights("applicationinsights");
}

// FeatBit Data Analytics Server (Python)
// Configured for internal access only - no external endpoint exposed in local development
// Only accessible by other services through service discovery
// For individual environment variables, parse the connection string to extract components
var (host, user, password, port) = PostgresConnectionStringParser.ParseConnectionStringWithPort(postgresConnectionString);
var dataAnalytics = builder.AddContainer("featbit-da-server", "featbit/featbit-data-analytics-server", "latest")
    .WithHttpEndpoint(targetPort: 80, name: "http", isProxied: false) // Internal only - no external port
    .WithEnvironment("POSTGRES_DATABASE", "featbit")
    .WithEnvironment("POSTGRES_PORT", "5432")
    .WithEnvironment("CHECK_DB_LIVNESS", "false")
    .WithEnvironment("DB_PROVIDER", "Postgres")
    .WithEnvironment("POSTGRES_HOST", host)
    .WithEnvironment("POSTGRES_PORT", port)
    .WithEnvironment("POSTGRES_USER", user)
    .WithEnvironment("POSTGRES_PASSWORD", password)
    // Monitoring and observability configuration
    .WithEnvironment("LOG_LEVEL", "INFO")
    .WithEnvironment("ENABLE_METRICS", "true")
    .WithEnvironment("METRICS_PORT", "9090")  // For Prometheus metrics if supported
    .WithEnvironment("HEALTH_CHECK_PATH", "/health")  // Standard health check endpoint
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;

        // Configure internal ingress - only accessible within the Container Apps environment
        containerApp.Configuration.Ingress.External = false;
    });
// Add Application Insights configuration only if enabled
if (applicationInsights != null)
{
    dataAnalytics = dataAnalytics
    .WithEnvironment("OTEL_SERVICE_NAME", "featbit-data-analytics")
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "service.name=featbit-data-analytics,service.version=1.0.0")
    .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsights.Resource.ConnectionStringExpression);
}

// FeatBit Web API Server (ASP.NET Core)
var webApi = builder.AddContainer("featbit-api", "featbit/featbit-api-server", "latest")
    .WithHttpEndpoint(port: 5000, targetPort: 5000, name: "http")
    .WithEnvironment("Postgres__ConnectionString", postgres.Resource.ConnectionStringExpression)
    .WithEnvironment("Redis__ConnectionString", redis.Resource.ConnectionStringExpression)
    .WithEnvironment("SSOEnabled", "true")
    .WithEnvironment("MqProvider", "Redis")
    .WithEnvironment("CacheProvider", "Redis")
    .WithEnvironment("DbProvider", "Postgres")
    .WithEnvironment("OLAP__ServiceHost", dataAnalytics.GetEndpoint("http"))
    .WithEnvironment("ASPNETCORE_URLS", "http://+:5000")
    .WithEnvironment("AllowedHosts", "*")
    .WithExternalHttpEndpoints()
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;
    });

// Add Application Insights configuration only if enabled
if (applicationInsights != null)
{
    webApi = webApi.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsights.Resource.ConnectionStringExpression);
}

// FeatBit Evaluation Server (ASP.NET Core)  
var evaluationServer = builder.AddContainer("featbit-evaluation-server", "featbit/featbit-evaluation-server", "latest")
    .WithHttpEndpoint(port: 5100, targetPort: 5100, name: "http")
    .WithEnvironment("Postgres__ConnectionString", postgres.Resource.ConnectionStringExpression)
    .WithEnvironment("Redis__ConnectionString", redis.Resource.ConnectionStringExpression)
    .WithEnvironment("SSOEnabled", "true")
    .WithEnvironment("MqProvider", "Redis")
    .WithEnvironment("CacheProvider", "Redis")
    .WithEnvironment("DbProvider", "Postgres")
    .WithEnvironment("ASPNETCORE_URLS", "http://+:5100")
    .WithEnvironment("AllowedHosts", "*")
    .WithExternalHttpEndpoints()
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;
    });
// Add Application Insights configuration only if enabled
if (applicationInsights != null)
{
    evaluationServer = evaluationServer.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsights.Resource.ConnectionStringExpression);
}

// FeatBit Angular UI
var angularUI = builder.AddContainer("featbit-ui", "featbit/featbit-ui", "latest")
    .WithHttpEndpoint(port: 8081, targetPort: 80, name: "http")
    .WithEnvironment("DEMO_URL", "https://featbit-samples.vercel.app")
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;
    });
// Configure API and Evaluation URLs for browser access (external URLs)
// In production, these should be replaced with actual external URLs
if (builder.ExecutionContext.IsPublishMode)
{
    angularUI = angularUI
        .WithEnvironment("API_URL", webApi.GetEndpoint("http"))
        .WithEnvironment("EVALUATION_URL", evaluationServer.GetEndpoint("http"))
        .WithExternalHttpEndpoints();
}
// For development, manually specify the localhost URLs that will be accessible from browser
else
{
    angularUI = angularUI
         .WithEnvironment("API_URL", "http://localhost:5000")
         .WithEnvironment("EVALUATION_URL", "http://localhost:5100");
}

builder.Build().Run();
