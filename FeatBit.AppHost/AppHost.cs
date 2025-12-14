using Aspire.Hosting.Azure;
using FeatBit.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

if (builder.ExecutionContext.IsPublishMode)
{
    // Add Azure Container Apps environment when publishing to Azure
    // This enables deployment to Azure Container Apps using 'azd up' command
    builder.AddAzureContainerAppEnvironment("featbit-aspire");
}

// Database Provider Configuration - choose between "Postgres" and "MongoDb"
var dbProvider = builder.Configuration["DbProvider"] ?? "Postgres";
var databaseConnectionString = builder.Configuration[$"ConnectionStrings:{dbProvider}"]
    ?? throw new InvalidOperationException($"{dbProvider} connection string is required");

// Redis Resource - use existing connection string
var redisConnectionString = builder.Configuration["ConnectionStrings:Redis"]
    ?? throw new InvalidOperationException("Redis connection string is required");

// Application Insights Resource - only use in publish mode (when deploying to Azure)
IResourceBuilder<IResourceWithConnectionString>? applicationInsights = null;
if (builder.ExecutionContext.IsPublishMode)
{
    // Azure Application Insights - for comprehensive monitoring and observability
    applicationInsights = builder.AddAzureApplicationInsights("applicationinsights");
}

// Add all services using extension methods from separate files
var dataAnalytics = builder.AddDataAnalyticsService(dbProvider, databaseConnectionString, applicationInsights);
var webApi = builder.AddWebApiService(dbProvider, databaseConnectionString, redisConnectionString, dataAnalytics, applicationInsights);
var evaluationServer = builder.AddEvaluationServerService(dbProvider, databaseConnectionString, redisConnectionString, applicationInsights);
var angularUI = builder.AddUIService(webApi, evaluationServer);

builder.Build().Run();
