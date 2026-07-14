#:package Aspire.Hosting.Azure.AppContainers@13.4.6
#:package Aspire.Hosting.PostgreSQL@13.4.6
#:package Aspire.Hosting.Redis@13.4.6
#:sdk Aspire.AppHost.Sdk@13.4.6
#:property Version=5.4.4

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

// FeatBit Aspire releases track the corresponding upstream FeatBit version.
const string FeatBitVersion = "5.4.4";
const string DatabaseName = "featbit";

var builder = DistributedApplication.CreateBuilder(args);
var isPublishMode = builder.ExecutionContext.IsPublishMode;
var useRedis = builder.Configuration.GetValue("FeatBit:UseRedis", false);
var enableOpenTelemetry = builder.Configuration.GetValue("FeatBit:OpenTelemetry:Enabled", true);
var useOpenTelemetryHeaders = builder.Configuration.GetValue("FeatBit:OpenTelemetry:UseHeaders", false);
var openTelemetryInsecure = builder.Configuration.GetValue(
    "FeatBit:OpenTelemetry:Insecure",
    defaultValue: false);

if (isPublishMode)
{
    // A single compute environment is inferred for all three FeatBit containers.
    builder.AddAzureContainerAppEnvironment("featbit-aca");
}

ReferenceExpression postgresConnectionString;
IResourceBuilder<PostgresDatabaseResource>? localPostgresDatabase = null;

if (isPublishMode)
{
    // Compose the Npgsql connection string expected by the two .NET backend services.
    var host = builder.AddParameter("postgres-host")
        .WithDescription("PostgreSQL server host name used by the Azure deployment.");
    var port = builder.AddParameter("postgres-port", "5432", publishValueAsDefault: true)
        .WithDescription("PostgreSQL server port.");
    var user = builder.AddParameter("postgres-user")
        .WithDescription("PostgreSQL login user.");
    var password = builder.AddParameter("postgres-password", secret: true)
        .WithDescription("PostgreSQL login password.");
    var connection = builder.AddConnectionString(
        "postgres",
        ReferenceExpression.Create(
            $"Host={host};Port={port};Username={user};Password={password};Database={DatabaseName}"));

    postgresConnectionString = connection.Resource.ConnectionStringExpression;
}
else
{
    var postgres = builder.AddPostgres("postgres")
        .WithImageTag("15.10")
        .WithDataVolume("featbit-postgres-data")
        .WithInitFiles("./infra/postgresql/docker-entrypoint-initdb.d")
        .WithLifetime(ContainerLifetime.Persistent);

    localPostgresDatabase = postgres.AddDatabase("featbit-db", DatabaseName);

    postgresConnectionString = localPostgresDatabase.Resource.ConnectionStringExpression;
}

ReferenceExpression? redisConnectionString = null;
IResourceBuilder<RedisResource>? localRedis = null;

if (useRedis)
{
    if (isPublishMode)
    {
        var redis = builder.AddConnectionString("redis");
        redisConnectionString = redis.Resource.ConnectionStringExpression;
    }
    else
    {
        // Redis is a derived FeatBit cache, while PostgreSQL is the source of truth.
        // Keep local Redis session-scoped so toggling Redis off and back on cannot reuse
        // FeatBit's stale, persistent `redis-is-populated` marker and skip cache backfill.
        localRedis = builder.AddRedis("redis");
        redisConnectionString = localRedis.Resource.ConnectionStringExpression;
    }
}

ReferenceExpression? openTelemetryEndpoint = null;
ReferenceExpression? openTelemetryHeaders = null;

if (isPublishMode && enableOpenTelemetry)
{
    var endpoint = builder.AddParameter("otel-exporter-otlp-endpoint")
        .WithDescription(
            "External OpenTelemetry Collector OTLP/gRPC endpoint used by the Azure deployment.");

    openTelemetryEndpoint = ReferenceExpression.Create($"{endpoint}");

    if (useOpenTelemetryHeaders)
    {
        var headers = builder.AddParameter("otel-exporter-otlp-headers", secret: true)
            .WithDescription(
                "Optional secret OTLP exporter headers, for example authorization metadata.");

        openTelemetryHeaders = ReferenceExpression.Create($"{headers}");
    }
}

var jwtKey = builder.AddParameter(
        "jwt-key",
        new GenerateParameterDefault { MinLength = 32 },
        secret: true,
        persist: true)
    .WithDescription("Auto-generated signing key for FeatBit API access tokens.");

var api = builder.AddContainer(
        "featbit-api",
        "featbit/featbit-api-server",
        FeatBitVersion)
    .WithEnvironment("DbProvider", "Postgres")
    .WithEnvironment("Postgres__ConnectionString", postgresConnectionString)
    .WithEnvironment("MqProvider", useRedis ? "Redis" : "Postgres")
    .WithEnvironment("CacheProvider", useRedis ? "Redis" : "None")
    .WithEnvironment("Jwt__Algorithm", "HS256")
    .WithEnvironment("Jwt__Key", jwtKey)
    .WithHttpEndpoint(
        port: isPublishMode ? (int?)null : 5000,
        targetPort: 5000,
        name: "http")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health/readiness");

ConfigureFeatBitOpenTelemetry(
    api,
    serviceName: "featbit-api",
    enableOpenTelemetry,
    isPublishMode,
    openTelemetryInsecure,
    openTelemetryEndpoint,
    openTelemetryHeaders);

var evaluation = builder.AddContainer(
        "featbit-evaluation",
        "featbit/featbit-evaluation-server",
        FeatBitVersion)
    .WithEnvironment("DbProvider", "Postgres")
    .WithEnvironment("Postgres__ConnectionString", postgresConnectionString)
    .WithEnvironment("MqProvider", useRedis ? "Redis" : "Postgres")
    .WithEnvironment("CacheProvider", useRedis ? "Redis" : "None")
    .WithHttpEndpoint(
        port: isPublishMode ? (int?)null : 5100,
        targetPort: 5100,
        name: "http")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health/readiness");

ConfigureFeatBitOpenTelemetry(
    evaluation,
    serviceName: "featbit-els",
    enableOpenTelemetry,
    isPublishMode,
    openTelemetryInsecure,
    openTelemetryEndpoint,
    openTelemetryHeaders);

if (localPostgresDatabase is not null)
{
    api.WaitFor(localPostgresDatabase);
    evaluation.WaitFor(localPostgresDatabase);
}

if (redisConnectionString is not null)
{
    api.WithEnvironment("Redis__ConnectionString", redisConnectionString);
    evaluation.WithEnvironment("Redis__ConnectionString", redisConnectionString);
}

if (localRedis is not null)
{
    api.WaitFor(localRedis);
    evaluation.WaitFor(localRedis);
}

// The UI writes these values into browser-side configuration. In local mode the browser
// must use the host proxy, not the Docker network alias. Azure resolves the endpoint
// references to the public HTTPS ingress URLs during deployment.
ReferenceExpression apiUrl = isPublishMode
    ? ReferenceExpression.Create($"{api.GetEndpoint("http")}")
    : ReferenceExpression.Create(
        $"http://localhost:{api.GetEndpoint("http").Property(EndpointProperty.Port)}");
ReferenceExpression evaluationUrl = isPublishMode
    ? ReferenceExpression.Create($"{evaluation.GetEndpoint("http")}")
    : ReferenceExpression.Create(
        $"http://localhost:{evaluation.GetEndpoint("http").Property(EndpointProperty.Port)}");

builder.AddContainer("featbit-ui", "featbit/featbit-ui", FeatBitVersion)
    .WithEnvironment("API_URL", apiUrl)
    .WithEnvironment("EVALUATION_URL", evaluationUrl)
    .WithEnvironment("DEMO_URL", "https://featbit-samples.vercel.app")
    .WithEnvironment("BASE_HREF", "/")
    .WithHttpEndpoint(
        port: isPublishMode ? (int?)null : 8081,
        targetPort: 80,
        name: "http")
    .WithExternalHttpEndpoints()
    .WaitFor(api)
    .WaitFor(evaluation);

builder.Build().Run();

static void ConfigureFeatBitOpenTelemetry(
    IResourceBuilder<ContainerResource> resource,
    string serviceName,
    bool enabled,
    bool isPublishMode,
    bool insecure,
    ReferenceExpression? endpoint,
    ReferenceExpression? headers)
{
    resource.WithEnvironment("ENABLE_OPENTELEMETRY", enabled ? "true" : "false");

    if (!enabled)
    {
        return;
    }

    if (isPublishMode)
    {
        if (endpoint is null)
        {
            throw new InvalidOperationException(
                "An OTLP endpoint is required when OpenTelemetry is enabled in publish mode.");
        }

        resource.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint);
    }
    else
    {
        // Raw container resources do not opt into the Aspire dashboard exporter automatically.
        resource.WithOtlpExporter(OtlpProtocol.Grpc);
    }

    resource
        .WithEnvironment("OTEL_SERVICE_NAME", serviceName)
        .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
        .WithEnvironment("OTEL_EXPORTER_OTLP_INSECURE", insecure ? "true" : "false");

    if (headers is not null)
    {
        resource.WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", headers);
    }
}
