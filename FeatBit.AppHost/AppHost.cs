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
var dataAnalytics = builder.AddContainer("featbit-da-server", "featbit/featbit-data-analytics-server", "latest")
    .WithHttpEndpoint(port: 8200, targetPort: 80, name: "http")
    .WithEnvironment("POSTGRES_DATABASE", "featbit")
    .WithEnvironment("POSTGRES_PORT", "5432")
    .WithEnvironment("CHECK_DB_LIVNESS", "false")
    .WithEnvironment("DB_PROVIDER", "Postgres");

// Configure PostgreSQL environment variables - parse connection string to extract components
// Use the connection string expression which works for both existing and new resources
dataAnalytics = dataAnalytics.WithEnvironment("Postgres__ConnectionString", postgres.Resource.ConnectionStringExpression);

// For individual environment variables, parse the connection string to extract components
// This works consistently for both existing connection strings and Azure PostgreSQL resources
if (useExistingPostgres)
{
    // For existing connection string, extract components from the provided connection string
    var connectionString = builder.Configuration["ConnectionStrings:Postgres"]!;
    var (host, user, password, port) = ParsePostgresConnectionStringWithPort(connectionString);

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
        var (host, user, password, port) = ParsePostgresConnectionStringWithPort(connectionString);

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
    .WithEnvironment("AllowedHosts", "*");

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
    .WithEnvironment("AllowedHosts", "*");

// FeatBit Angular UI
var angularUI = builder.AddContainer("featbit-ui", "featbit/featbit-ui", "latest")
    .WithHttpEndpoint(port: 8081, targetPort: 80, name: "http")
    .WithEnvironment("DEMO_URL", "https://featbit-samples.vercel.app");

// Configure API and Evaluation URLs for browser access (external URLs)
// For development, manually specify the localhost URLs that will be accessible from browser
// In production, these should be replaced with actual external URLs
angularUI = angularUI
        // .WithEnvironment("API_URL", "http://localhost:5000")     
        // .WithEnvironment("EVALUATION_URL", "http://localhost:5100"); 
    .WithEnvironment("API_URL", webApi.GetEndpoint("http"))     
    .WithEnvironment("EVALUATION_URL", evaluationServer.GetEndpoint("http")); 

builder.Build().Run();



// Helper method to parse PostgreSQL connection string with port
static (string host, string? user, string? password, string port) ParsePostgresConnectionStringWithPort(string connectionString)
{
    var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
    string host = "localhost";
    string? user = null;
    string? password = null;
    string port = "5432";

    foreach (var part in parts)
    {
        var keyValue = part.Split('=', 2);
        if (keyValue.Length == 2)
        {
            var key = keyValue[0].Trim().ToLowerInvariant();
            var value = keyValue[1].Trim();

            switch (key)
            {
                case "host":
                case "server":
                    host = value;
                    break;
                case "port":
                    port = value;
                    break;
                case "username":
                case "user":
                case "uid":
                case "user id":
                    user = value;
                    break;
                case "password":
                case "pwd":
                    password = value;
                    break;
            }
        }
    }

    return (host, user, password, port);
}
