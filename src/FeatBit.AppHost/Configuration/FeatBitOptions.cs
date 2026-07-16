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

public sealed record FeatBitOpenTelemetryOptions(
    bool Enabled,
    bool UseHeaders,
    bool Insecure);

public sealed record FeatBitAzureOptions(int MinReplicas, int MaxReplicas);

public sealed record FeatBitOptions(
    string Version,
    bool IsPublishMode,
    bool UseRedis,
    FeatBitJwtOptions Jwt,
    FeatBitOpenTelemetryOptions OpenTelemetry,
    FeatBitAzureOptions Azure)
{
    private const int DefaultAzureMinReplicas = 1;
    private const int DefaultAzureMaxReplicas = 10;

    public static FeatBitOptions Load(IDistributedApplicationBuilder builder)
    {
        var version = GetVersion(builder);
        ValidateVersion(version);

        var configuration = builder.Configuration;
        var isPublishMode = builder.ExecutionContext.IsPublishMode;
        var jwt = LoadJwt(configuration, isPublishMode);
        var azure = LoadAzure(configuration, isPublishMode);

        return new FeatBitOptions(
            version,
            isPublishMode,
            configuration.GetValue("FeatBit:UseRedis", false),
            jwt,
            new FeatBitOpenTelemetryOptions(
                configuration.GetValue("FeatBit:OpenTelemetry:Enabled", true),
                configuration.GetValue("FeatBit:OpenTelemetry:UseHeaders", false),
                configuration.GetValue("FeatBit:OpenTelemetry:Insecure", false)),
            azure);
    }

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

    private static FeatBitAzureOptions LoadAzure(
        IConfiguration configuration,
        bool isPublishMode)
    {
        if (!isPublishMode)
        {
            return new FeatBitAzureOptions(
                DefaultAzureMinReplicas,
                DefaultAzureMaxReplicas);
        }

        var minReplicas = configuration.GetValue(
            "FeatBit:Azure:MinReplicas",
            DefaultAzureMinReplicas);
        var maxReplicas = configuration.GetValue(
            "FeatBit:Azure:MaxReplicas",
            DefaultAzureMaxReplicas);

        if (minReplicas is < 0 or > 1000)
        {
            throw new InvalidOperationException(
                "FeatBit:Azure:MinReplicas must be between 0 and 1000.");
        }

        if (maxReplicas is < 1 or > 1000 || minReplicas > maxReplicas)
        {
            throw new InvalidOperationException(
                "FeatBit:Azure:MaxReplicas must be between 1 and 1000 and not less than MinReplicas.");
        }

        return new FeatBitAzureOptions(minReplicas, maxReplicas);
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
                $"FeatBit version '{version}' is not a safe Git tag or cache directory name.");
        }
    }
}
