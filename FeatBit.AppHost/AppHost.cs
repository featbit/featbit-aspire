using FeatBit.AppHost.Utils;
using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

// Check configuration to determine resource mode
var useExistingPostgres = builder.Configuration["UseExisting:Postgres"]?.ToLowerInvariant() == "true";
var useExistingRedis = builder.Configuration["UseExisting:Redis"]?.ToLowerInvariant() == "true";

// PostgreSQL Resource - create new Azure resource or use existing connection string
IResourceBuilder<IResourceWithConnectionString> postgres;
if (useExistingPostgres)
{
    var postgresConnectionString = builder.Configuration["ConnectionStrings:Postgres"]
        ?? throw new InvalidOperationException("Postgres connection string is required when UseExisting:Postgres is true");
    postgres = builder.AddConnectionString("postgres", postgresConnectionString);
}
else
{
    // Azure PostgreSQL - for production use Azure PostgreSQL Flexible Server
    postgres = builder.AddAzurePostgresFlexibleServer("postgres")
        .AddDatabase("featbit");
}

// Redis Resource - create new Azure resource or use existing connection string
IResourceBuilder<IResourceWithConnectionString> redis;
if (useExistingRedis)
{
    var redisConnectionString = builder.Configuration["ConnectionStrings:Redis"]
        ?? throw new InvalidOperationException("Redis connection string is required when UseExisting:Redis is true");
    redis = builder.AddConnectionString("redis", redisConnectionString);
}
else
{
    // Azure Redis - supports both Azure Cache for Redis and Azure Managed Redis
    redis = builder.AddAzureRedis("redis");
}

// FeatBit Data Analytics Server (Python)
// Configured for internal access only - no external endpoint exposed in local development
// Only accessible by other services through service discovery
var dataAnalytics = builder.AddContainer("featbit-da-server", "featbit/featbit-data-analytics-server", "latest")
    .WithHttpEndpoint(targetPort: 80, name: "http", isProxied: false) // Internal only - no external port
    .WithEnvironment("POSTGRES_DATABASE", "featbit")
    .WithEnvironment("POSTGRES_PORT", "5432")
    .WithEnvironment("CHECK_DB_LIVNESS", "false")
    .WithEnvironment("DB_PROVIDER", "Postgres")
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;
        
        // Configure internal ingress - only accessible within the Container Apps environment
        containerApp.Configuration.Ingress.External = false;
    });

// Configure PostgreSQL environment variables - parse connection string to extract components
// Use the connection string expression which works for both existing and new resources
dataAnalytics = dataAnalytics.WithEnvironment("Postgres__ConnectionString", postgres.Resource.ConnectionStringExpression);

// For individual environment variables, parse the connection string to extract components
// This works consistently for both existing connection strings and Azure PostgreSQL resources
if (useExistingPostgres)
{
    // For existing connection string, extract components from the provided connection string
    var connectionString = builder.Configuration["ConnectionStrings:Postgres"]!;
    var (host, user, password, port) = PostgresConnectionStringParser.ParseConnectionStringWithPort(connectionString);

    dataAnalytics = dataAnalytics
        .WithEnvironment("POSTGRES_HOST", host)
        .WithEnvironment("POSTGRES_PORT", port);

    if (!string.IsNullOrEmpty(user))
    {
        dataAnalytics = dataAnalytics.WithEnvironment("POSTGRES_USER", user);
    }
    if (!string.IsNullOrEmpty(password))
    {
        dataAnalytics = dataAnalytics.WithEnvironment("POSTGRES_PASSWORD", password);
    }
}
else
{
    // For Azure PostgreSQL Flexible Server, extract components from connection string expression
    // We need to use a callback to parse the connection string when it's available
    dataAnalytics = dataAnalytics.WithEnvironment(context =>
    {
        var connectionString = postgres.Resource.ConnectionStringExpression.ValueExpression;
        var (host, user, password, port) = PostgresConnectionStringParser.ParseConnectionStringWithPort(connectionString);

        context.EnvironmentVariables["POSTGRES_HOST"] = host;
        context.EnvironmentVariables["POSTGRES_PORT"] = port;
        if (!string.IsNullOrEmpty(user))
        {
            context.EnvironmentVariables["POSTGRES_USER"] = user;
        }
        if (!string.IsNullOrEmpty(password))
        {
            context.EnvironmentVariables["POSTGRES_PASSWORD"] = password;
        }
    });
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
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;
    });

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
    .PublishAsAzureContainerApp((infrastructure, containerApp) =>
    {
        // Set minimum replicas to 3 for high availability
        containerApp.Template.Scale.MinReplicas = 3;
        // Optionally set maximum replicas
        containerApp.Template.Scale.MaxReplicas = 10;
    });

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
// For development, manually specify the localhost URLs that will be accessible from browser
// In production, these should be replaced with actual external URLs
angularUI = angularUI
         .WithEnvironment("API_URL", "http://localhost:5000")
         .WithEnvironment("EVALUATION_URL", "http://localhost:5100"); 
    //.WithEnvironment("API_URL", webApi.GetEndpoint("http"))     
    //.WithEnvironment("EVALUATION_URL", evaluationServer.GetEndpoint("http")); 

builder.Build().Run();
