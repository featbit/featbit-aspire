using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public enum FeatBitService
{
    Ui,
    Api,
    Els
}

public sealed record FeatBitServiceConfigurationResources(
    IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> SecretParameters);

public static class FeatBitServiceConfigurationExtensions
{
    private static readonly HashSet<string> CommonBackendManagedEnvironment = new(
        [
            "DbProvider",
            "MqProvider",
            "CacheProvider",
            "Postgres__ConnectionString",
            "Redis__ConnectionString",
            "ENABLE_OPENTELEMETRY",
            "OTEL_EXPORTER_OTLP_ENDPOINT",
            "OTEL_EXPORTER_OTLP_HEADERS",
            "OTEL_EXPORTER_OTLP_INSECURE",
            "OTEL_EXPORTER_OTLP_PROTOCOL",
            "OTEL_SERVICE_NAME",
            "VERSION"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UiManagedEnvironment = new(
        ["API_URL", "EVALUATION_URL", "VERSION"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ApiManagedEnvironment = new(
        CommonBackendManagedEnvironment.Concat(
        [
            "Jwt__Algorithm",
            "Jwt__Key",
            "Jwt__PrivateKeyPath",
            "Jwt__PublicKeyPath"
        ]),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ReservedResourceNames = new(
        [
            "featbit-aca",
            "featbit-api",
            "featbit-evaluation",
            "featbit-ui",
            "featbit",
            "postgres-connection",
            "postgres-host",
            "postgres-port",
            "postgres-user",
            "postgres-database",
            "postgres-password",
            "redis",
            "redis-connection",
            "jwt-key",
            "jwt-private-key",
            "jwt-public-key",
            "otel-exporter-otlp-endpoint",
            "otel-exporter-otlp-headers"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static FeatBitServiceConfigurationResources AddFeatBitServiceConfiguration(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        var services = new[]
        {
            (Service: FeatBitService.Ui, Options: options.Ui),
            (Service: FeatBitService.Api, Options: options.Api),
            (Service: FeatBitService.Els, Options: options.Els)
        };

        foreach (var (service, serviceOptions) in services)
        {
            ValidateManagedEnvironment(service, serviceOptions);
        }

        var usagesByParameter = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (service, serviceOptions) in services)
        {
            foreach (var (environmentVariable, parameterName) in serviceOptions.SecretParameters)
            {
                if (ReservedResourceNames.Contains(parameterName))
                {
                    throw new InvalidOperationException(
                        $"FeatBit:{service}:SecretParameters:{environmentVariable} references " +
                        $"reserved Aspire resource name '{parameterName}'. Choose a service-specific name.");
                }

                if (!usagesByParameter.TryGetValue(parameterName, out var usages))
                {
                    usages = [];
                    usagesByParameter.Add(parameterName, usages);
                }

                usages.Add($"{service}/{environmentVariable}");
            }
        }

        var parameters = new Dictionary<string, IResourceBuilder<ParameterResource>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (parameterName, usages) in usagesByParameter.OrderBy(entry => entry.Key))
        {
            parameters.Add(
                parameterName,
                builder.AddParameter(parameterName, secret: true)
                    .WithDescription(
                        $"Secret FeatBit service environment value used by {string.Join(", ", usages)}."));
        }

        return new FeatBitServiceConfigurationResources(parameters);
    }

    public static IResourceBuilder<ContainerResource> WithFeatBitServiceConfiguration(
        this IResourceBuilder<ContainerResource> resource,
        FeatBitService service,
        FeatBitServiceOptions options,
        FeatBitServiceConfigurationResources configuration,
        string version)
    {
        foreach (var (name, value) in options.Environment.OrderBy(entry => entry.Key))
        {
            resource.WithEnvironment(name, value);
        }

        foreach (var (name, parameterName) in options.SecretParameters.OrderBy(entry => entry.Key))
        {
            resource.WithEnvironment(
                name,
                configuration.SecretParameters.TryGetValue(parameterName, out var parameter)
                    ? parameter
                    : throw new InvalidOperationException(
                        $"Secret parameter '{parameterName}' for FeatBit {service} was not registered."));
        }

        return resource.WithEnvironment("VERSION", version);
    }

    private static void ValidateManagedEnvironment(
        FeatBitService service,
        FeatBitServiceOptions options)
    {
        if (service == FeatBitService.Ui && options.SecretParameters.Count > 0)
        {
            throw new InvalidOperationException(
                "FeatBit:Ui:SecretParameters cannot be used because UI configuration is browser-visible.");
        }

        var managedEnvironment = service switch
        {
            FeatBitService.Ui => UiManagedEnvironment,
            FeatBitService.Api => ApiManagedEnvironment,
            FeatBitService.Els => CommonBackendManagedEnvironment,
            _ => throw new ArgumentOutOfRangeException(nameof(service), service, null)
        };

        var configuredNames = options.Environment.Keys
            .Concat(options.SecretParameters.Keys);
        var managedName = configuredNames.FirstOrDefault(managedEnvironment.Contains);
        if (managedName is not null)
        {
            throw new InvalidOperationException(
                $"FeatBit:{service} environment variable '{managedName}' is managed by the AppHost " +
                "and cannot be overridden through service configuration.");
        }
    }
}
