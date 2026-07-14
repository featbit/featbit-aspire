#:package Aspire.Hosting.Azure.AppContainers@13.4.6
#:package Aspire.Hosting.PostgreSQL@13.4.6
#:package Aspire.Hosting.Redis@13.4.6
#:sdk Aspire.AppHost.Sdk@13.4.6
#:property Version=5.4.4

using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Microsoft.Extensions.Configuration;

// FeatBit Aspire releases track the corresponding upstream FeatBit version.
const string FeatBitVersion = "5.4.4";
const string DatabaseName = "featbit";
const string JwtAlgorithmHs256 = "HS256";
const string JwtAlgorithmRs256 = "RS256";
const string JwtAlgorithmEs256 = "ES256";
const string JwtPrivateKeyContainerPath = "/app/secrets/jwt/private.pem";
const string JwtPublicKeyContainerPath = "/app/secrets/jwt/public.pem";
const int DefaultAzureMinReplicas = 1;
const int DefaultAzureMaxReplicas = 10;

var builder = DistributedApplication.CreateBuilder(args);
var isPublishMode = builder.ExecutionContext.IsPublishMode;
var useRedis = builder.Configuration.GetValue("FeatBit:UseRedis", false);
var jwtAlgorithm = (builder.Configuration["FeatBit:Jwt:Algorithm"] ?? JwtAlgorithmHs256)
    .Trim()
    .ToUpperInvariant();
var enableOpenTelemetry = builder.Configuration.GetValue("FeatBit:OpenTelemetry:Enabled", true);
var useOpenTelemetryHeaders = builder.Configuration.GetValue("FeatBit:OpenTelemetry:UseHeaders", false);
var openTelemetryInsecure = builder.Configuration.GetValue(
    "FeatBit:OpenTelemetry:Insecure",
    defaultValue: false);
var azureMinReplicas = DefaultAzureMinReplicas;
var azureMaxReplicas = DefaultAzureMaxReplicas;

if (jwtAlgorithm is not JwtAlgorithmHs256 and not JwtAlgorithmRs256 and not JwtAlgorithmEs256)
{
    throw new InvalidOperationException(
        $"Unsupported FeatBit JWT algorithm '{jwtAlgorithm}'. " +
        $"Supported values are {JwtAlgorithmHs256}, {JwtAlgorithmRs256}, and {JwtAlgorithmEs256}.");
}

if (isPublishMode)
{
    azureMinReplicas = builder.Configuration.GetValue(
        "FeatBit:Azure:MinReplicas",
        DefaultAzureMinReplicas);
    azureMaxReplicas = builder.Configuration.GetValue(
        "FeatBit:Azure:MaxReplicas",
        DefaultAzureMaxReplicas);

    ValidateAzureReplicaRange(azureMinReplicas, azureMaxReplicas);

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

IResourceBuilder<ParameterResource>? jwtKey = null;
IResourceBuilder<ParameterResource>? jwtPrivateKey = null;
IResourceBuilder<ParameterResource>? jwtPublicKey = null;
string? localJwtPrivateKeyPath = null;
string? localJwtPublicKeyPath = null;

if (jwtAlgorithm == JwtAlgorithmHs256)
{
    jwtKey = builder.AddParameter(
            "jwt-key",
            new GenerateParameterDefault { MinLength = 64 },
            secret: true,
            persist: true)
        .WithDescription("Auto-generated HS256 signing key for FeatBit API access tokens.");
}
else if (isPublishMode)
{
    jwtPrivateKey = builder.AddParameter("jwt-private-key", secret: true)
        .WithDescription($"PEM-encoded {jwtAlgorithm} private key used to sign FeatBit API access tokens.");
    jwtPublicKey = builder.AddParameter("jwt-public-key", secret: true)
        .WithDescription($"PEM-encoded {jwtAlgorithm} public key used to verify FeatBit API access tokens.");
}
else
{
    localJwtPrivateKeyPath = GetRequiredJwtKeyPath(
        builder.Configuration,
        "FeatBit:Jwt:PrivateKeyPath",
        jwtAlgorithm);
    localJwtPublicKeyPath = GetRequiredJwtKeyPath(
        builder.Configuration,
        "FeatBit:Jwt:PublicKeyPath",
        jwtAlgorithm);
}

var api = builder.AddContainer(
        "featbit-api",
        "featbit/featbit-api-server",
        FeatBitVersion)
    .WithEnvironment("DbProvider", "Postgres")
    .WithEnvironment("Postgres__ConnectionString", postgresConnectionString)
    .WithEnvironment("MqProvider", useRedis ? "Redis" : "Postgres")
    .WithEnvironment("CacheProvider", useRedis ? "Redis" : "None")
    .WithEnvironment("Jwt__Algorithm", jwtAlgorithm)
    .WithHttpEndpoint(
        port: isPublishMode ? (int?)null : 5000,
        targetPort: 5000,
        name: "http")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health/readiness");

if (jwtAlgorithm == JwtAlgorithmHs256)
{
    api.WithEnvironment("Jwt__Key", jwtKey!);
}
else
{
    api
        .WithEnvironment("Jwt__PrivateKeyPath", JwtPrivateKeyContainerPath)
        .WithEnvironment("Jwt__PublicKeyPath", JwtPublicKeyContainerPath);

    if (!isPublishMode)
    {
        api
            .WithBindMount(localJwtPrivateKeyPath!, JwtPrivateKeyContainerPath, isReadOnly: true)
            .WithBindMount(localJwtPublicKeyPath!, JwtPublicKeyContainerPath, isReadOnly: true);
    }
}

if (isPublishMode)
{
    api.PublishAsAzureContainerApp((infrastructure, app) =>
    {
        ConfigureAzureContainerAppReplicas(app, azureMinReplicas, azureMaxReplicas);

        if (jwtAlgorithm == JwtAlgorithmHs256)
        {
            return;
        }

        const string privateKeySecretName = "jwt-private-key";
        const string publicKeySecretName = "jwt-public-key";
        const string keyVolumeName = "jwt-keys";

        var privateKeyValue = jwtPrivateKey!.AsProvisioningParameter(
            infrastructure,
            "jwtPrivateKey");
        var publicKeyValue = jwtPublicKey!.AsProvisioningParameter(
            infrastructure,
            "jwtPublicKey");

        app.Configuration.Secrets.Add(new ContainerAppWritableSecret
        {
            Name = privateKeySecretName,
            Value = privateKeyValue
        });
        app.Configuration.Secrets.Add(new ContainerAppWritableSecret
        {
            Name = publicKeySecretName,
            Value = publicKeyValue
        });

        var keyVolume = new ContainerAppVolume
        {
            Name = keyVolumeName,
            StorageType = ContainerAppStorageType.Secret
        };
        keyVolume.Secrets.Add(new SecretVolumeItem
        {
            SecretRef = privateKeySecretName,
            Path = "private.pem"
        });
        keyVolume.Secrets.Add(new SecretVolumeItem
        {
            SecretRef = publicKeySecretName,
            Path = "public.pem"
        });
        app.Template.Volumes.Add(keyVolume);
        app.Template.Containers[0].Unwrap().VolumeMounts.Add(new ContainerAppVolumeMount
        {
            VolumeName = keyVolumeName,
            MountPath = "/app/secrets/jwt"
        });
    });
}

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

if (isPublishMode)
{
    evaluation.PublishAsAzureContainerApp((_, app) =>
        ConfigureAzureContainerAppReplicas(app, azureMinReplicas, azureMaxReplicas));
}

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

var ui = builder.AddContainer("featbit-ui", "featbit/featbit-ui", FeatBitVersion)
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

if (isPublishMode)
{
    ui.PublishAsAzureContainerApp((_, app) =>
        ConfigureAzureContainerAppReplicas(app, azureMinReplicas, azureMaxReplicas));
}

builder.Build().Run();

static void ValidateAzureReplicaRange(int minReplicas, int maxReplicas)
{
    if (minReplicas is < 0 or > 1000)
    {
        throw new InvalidOperationException(
            "FeatBit:Azure:MinReplicas must be between 0 and 1000.");
    }

    if (maxReplicas is < 1 or > 1000)
    {
        throw new InvalidOperationException(
            "FeatBit:Azure:MaxReplicas must be between 1 and 1000.");
    }

    if (minReplicas > maxReplicas)
    {
        throw new InvalidOperationException(
            "FeatBit:Azure:MinReplicas cannot be greater than FeatBit:Azure:MaxReplicas.");
    }
}

static void ConfigureAzureContainerAppReplicas(
    ContainerApp app,
    int minReplicas,
    int maxReplicas)
{
    app.Template.Scale = new ContainerAppScale
    {
        MinReplicas = minReplicas,
        MaxReplicas = maxReplicas
    };
}

static string GetRequiredJwtKeyPath(
    IConfiguration configuration,
    string configurationKey,
    string algorithm)
{
    var configuredPath = configuration[configurationKey];
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
        throw new InvalidOperationException(
            $"{configurationKey} is required when FeatBit:Jwt:Algorithm is {algorithm}.");
    }

    var fullPath = Path.GetFullPath(configuredPath);
    if (!File.Exists(fullPath))
    {
        throw new FileNotFoundException(
            $"The JWT key file configured by {configurationKey} was not found.",
            fullPath);
    }

    return fullPath;
}

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
