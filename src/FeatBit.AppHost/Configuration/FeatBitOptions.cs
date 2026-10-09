using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace FeatBit.AppHost;

public enum FeatBitJwtAlgorithm
{
    HS256,
    RS256,
    ES256
}

public sealed record FeatBitJwtOptions(
    FeatBitJwtAlgorithm Algorithm,
    string? PrivateKeyPath,
    string? PublicKeyPath);

public enum FeatBitOpenTelemetryExportTarget
{
    ExternalCollector,
    AzureDashboard
}

public sealed record FeatBitOpenTelemetryOptions(
    bool Enabled,
    bool UseHeaders,
    bool Insecure,
    FeatBitOpenTelemetryExportTarget ExportTarget = FeatBitOpenTelemetryExportTarget.ExternalCollector);

public sealed record FeatBitAzureScaleOptions(int MinReplicas, int MaxReplicas);

public sealed record FeatBitAzureOptions(
    FeatBitAzureScaleOptions Ui,
    FeatBitAzureScaleOptions Api,
    FeatBitAzureScaleOptions Els);

public sealed record FeatBitServiceOptions(
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> SecretParameters);

public sealed record FeatBitOptions(
    string Version,
    bool IsPublishMode,
    bool UseRedis,
    FeatBitServiceOptions Ui,
    FeatBitServiceOptions Api,
    FeatBitServiceOptions Els,
    FeatBitJwtOptions Jwt,
    FeatBitOpenTelemetryOptions OpenTelemetry,
    FeatBitAzureOptions Azure)
{
    public bool UseLocalInfrastructure { get; init; }
    public string? UiApiUrl { get; init; }
    public string? UiEvaluationUrl { get; init; }

    private const int DefaultAzureUiMinReplicas = 1;
    private const int DefaultAzureUiMaxReplicas = 3;
    private const int DefaultAzureApiMinReplicas = 3;
    private const int DefaultAzureApiMaxReplicas = 10;
    private const int DefaultAzureElsMinReplicas = 3;
    private const int DefaultAzureElsMaxReplicas = 10;

    public static FeatBitOptions Load(IDistributedApplicationBuilder builder)
    {
        var version = GetVersion(builder);
        ValidateVersion(version);

        var configuration = builder.Configuration;
        var isPublishMode = builder.ExecutionContext.IsPublishMode;
        var useLocalInfrastructure = !isPublishMode &&
            configuration.GetValue("FeatBit:UseLocalInfrastructure", true);
        var jwt = LoadJwt(configuration, isPublishMode);
        var azure = LoadAzure(configuration);

        return new FeatBitOptions(
            version,
            isPublishMode,
            configuration.GetValue("FeatBit:UseRedis", useLocalInfrastructure),
            LoadService(
                configuration,
                "Ui",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DEMO_URL"] = "https://featbit-samples.vercel.app",
                    ["BASE_HREF"] = "/",
                    ["HOSTING_MODE"] = isPublishMode ? "saas" : "self-hosted"
                }),
            FeatBitAuthenticationConfiguration.Apply(
                configuration,
                LoadService(configuration, "Api")),
            LoadService(configuration, "Els"),
            jwt,
            LoadOpenTelemetry(configuration),
            azure)
        {
            UseLocalInfrastructure = useLocalInfrastructure,
            UiApiUrl = LoadBrowserUrl(configuration, "FeatBit:Ui:ApiUrl"),
            UiEvaluationUrl = LoadBrowserUrl(configuration, "FeatBit:Ui:EvaluationUrl")
        };
    }

    private static FeatBitOpenTelemetryOptions LoadOpenTelemetry(IConfiguration configuration)
    {
        var configuredTarget = configuration["FeatBit:OpenTelemetry:ExportTarget"] ?? "ExternalCollector";
        var target = configuredTarget.Trim().ToLowerInvariant() switch
        {
            "externalcollector" => FeatBitOpenTelemetryExportTarget.ExternalCollector,
            "azuredashboard" => FeatBitOpenTelemetryExportTarget.AzureDashboard,
            _ => throw new InvalidOperationException(
                $"Unsupported FeatBit:OpenTelemetry:ExportTarget '{configuredTarget}'. " +
                "Supported values are ExternalCollector and AzureDashboard.")
        };

        return new FeatBitOpenTelemetryOptions(
            configuration.GetValue("FeatBit:OpenTelemetry:Enabled", true),
            configuration.GetValue("FeatBit:OpenTelemetry:UseHeaders", false),
            configuration.GetValue("FeatBit:OpenTelemetry:Insecure", false),
            target);
    }

    private static string? LoadBrowserUrl(IConfiguration configuration, string configurationKey)
    {
        var value = configuration[configurationKey]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.IsWellFormedOriginalString() ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{configurationKey} must be an absolute HTTP(S) URL without credentials, a query string, or a fragment.");
        }

        // The UI appends API paths beginning with '/', so avoid double slashes.
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static FeatBitServiceOptions LoadService(
        IConfiguration configuration,
        string serviceName,
        IReadOnlyDictionary<string, string>? environmentDefaults = null)
    {
        var environment = new Dictionary<string, string>(
            environmentDefaults ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        AddFlattenedValues(
            configuration.GetSection($"FeatBit:{serviceName}:Environment"),
            environment);

        var secretParameters = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        AddFlattenedValues(
            configuration.GetSection($"FeatBit:{serviceName}:SecretParameters"),
            secretParameters);

        var duplicateTarget = environment.Keys.FirstOrDefault(
            secretParameters.ContainsKey);
        if (duplicateTarget is not null)
        {
            throw new InvalidOperationException(
                $"FeatBit:{serviceName} environment variable '{duplicateTarget}' is configured " +
                "as both a non-secret value and a secret parameter.");
        }

        foreach (var (environmentVariable, parameterName) in secretParameters)
        {
            ValidateParameterName(
                parameterName,
                $"FeatBit:{serviceName}:SecretParameters:{environmentVariable}");
        }

        return new FeatBitServiceOptions(environment, secretParameters);
    }

    private static void AddFlattenedValues(
        IConfigurationSection section,
        IDictionary<string, string> destination)
    {
        var prefix = section.Path + ConfigurationPath.KeyDelimiter;
        var configuredPaths = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in section.AsEnumerable())
        {
            if (entry.Value is null ||
                !entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entry.Key[prefix.Length..];
            var environmentVariable = relativePath
                .Replace(
                    ConfigurationPath.KeyDelimiter,
                    "__",
                    StringComparison.Ordinal);
            ValidateEnvironmentVariableName(environmentVariable, section.Path);

            if (configuredPaths.TryGetValue(environmentVariable, out var existingPath))
            {
                var existingIsHierarchical = existingPath.Contains(
                    ConfigurationPath.KeyDelimiter,
                    StringComparison.Ordinal);
                var candidateIsHierarchical = relativePath.Contains(
                    ConfigurationPath.KeyDelimiter,
                    StringComparison.Ordinal);

                // Environment variables turn `__` into configuration path delimiters.
                // Prefer that hierarchical form so it can override a JSON key written
                // in the native container environment form, such as Cors__Enabled.
                if (candidateIsHierarchical && !existingIsHierarchical)
                {
                    configuredPaths[environmentVariable] = relativePath;
                    destination[environmentVariable] = entry.Value;
                }
                else if (candidateIsHierarchical == existingIsHierarchical &&
                         !relativePath.Equals(existingPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{section.Path} defines ambiguous environment variable " +
                        $"'{environmentVariable}' through '{existingPath}' and '{relativePath}'.");
                }

                continue;
            }

            configuredPaths.Add(environmentVariable, relativePath);
            destination[environmentVariable] = entry.Value;
        }
    }

    private static void ValidateEnvironmentVariableName(
        string environmentVariable,
        string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(environmentVariable) ||
            environmentVariable.Any(character =>
                !IsAsciiLetterOrDigit(character) && character is not ('_' or '.')))
        {
            throw new InvalidOperationException(
                $"{configurationPath} contains invalid environment variable name " +
                $"'{environmentVariable}'. Use letters, digits, underscores, or periods.");
        }
    }

    private static void ValidateParameterName(
        string parameterName,
        string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(parameterName) ||
            parameterName.Length > 63 ||
            !IsAsciiLowercaseLetterOrDigit(parameterName[0]) ||
            !IsAsciiLowercaseLetterOrDigit(parameterName[^1]) ||
            parameterName.Any(character =>
                !IsAsciiLowercaseLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException(
                $"{configurationPath} must reference a lowercase Aspire parameter name " +
                "containing only letters, digits, or hyphens (maximum 63 characters).");
        }
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsAsciiLowercaseLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string GetVersion(IDistributedApplicationBuilder builder)
    {
        var informationalVersion = builder.AppHostAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException(
                "The AppHost project Version property must contain the target FeatBit version.");
        }

        return informationalVersion.Split('+', 2)[0];
    }

    private static FeatBitJwtOptions LoadJwt(
        IConfiguration configuration,
        bool isPublishMode)
    {
        var configuredAlgorithm = configuration["FeatBit:Jwt:Algorithm"] ?? "HS256";
        if (!Enum.TryParse<FeatBitJwtAlgorithm>(
                configuredAlgorithm.Trim(),
                ignoreCase: true,
                out var algorithm))
        {
            throw new InvalidOperationException(
                $"Unsupported FeatBit JWT algorithm '{configuredAlgorithm}'. " +
                "Supported values are HS256, RS256, and ES256.");
        }

        if (algorithm == FeatBitJwtAlgorithm.HS256 || isPublishMode)
        {
            return new FeatBitJwtOptions(algorithm, null, null);
        }

        return new FeatBitJwtOptions(
            algorithm,
            GetRequiredFile(configuration, "FeatBit:Jwt:PrivateKeyPath", algorithm),
            GetRequiredFile(configuration, "FeatBit:Jwt:PublicKeyPath", algorithm));
    }

    private static FeatBitAzureOptions LoadAzure(IConfiguration configuration)
    {
        return new FeatBitAzureOptions(
            LoadAzureScale(
                configuration,
                "Ui",
                DefaultAzureUiMinReplicas,
                DefaultAzureUiMaxReplicas),
            LoadAzureScale(
                configuration,
                "Api",
                DefaultAzureApiMinReplicas,
                DefaultAzureApiMaxReplicas),
            LoadAzureScale(
                configuration,
                "Els",
                DefaultAzureElsMinReplicas,
                DefaultAzureElsMaxReplicas));
    }

    private static FeatBitAzureScaleOptions LoadAzureScale(
        IConfiguration configuration,
        string serviceName,
        int defaultMinReplicas,
        int defaultMaxReplicas)
    {
        var configurationPath = $"FeatBit:Azure:{serviceName}";
        var minReplicas = configuration.GetValue(
            $"{configurationPath}:MinReplicas",
            defaultMinReplicas);
        var maxReplicas = configuration.GetValue(
            $"{configurationPath}:MaxReplicas",
            defaultMaxReplicas);

        if (minReplicas is < 0 or > 1000)
        {
            throw new InvalidOperationException(
                $"{configurationPath}:MinReplicas must be between 0 and 1000.");
        }

        if (maxReplicas is < 1 or > 1000 || minReplicas > maxReplicas)
        {
            throw new InvalidOperationException(
                $"{configurationPath}:MaxReplicas must be between 1 and 1000 and not less than MinReplicas.");
        }

        return new FeatBitAzureScaleOptions(minReplicas, maxReplicas);
    }

    private static string GetRequiredFile(
        IConfiguration configuration,
        string configurationKey,
        FeatBitJwtAlgorithm algorithm)
    {
        var configuredPath = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                $"{configurationKey} is required when FeatBit:Jwt:Algorithm is {algorithm}.");
        }

        var fullPath = Path.GetFullPath(configuredPath);
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException(
                $"The JWT key file configured by {configurationKey} was not found.",
                fullPath);
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            version is "." or ".." ||
            version.Any(character =>
                !char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_' or '+')))
        {
            throw new InvalidOperationException(
                $"FeatBit version '{version}' is invalid.");
        }
    }
}
