using Microsoft.Extensions.Configuration;

namespace FeatBit.AppHost;

public static class FeatBitAuthenticationConfiguration
{
    private const string ConfigurationPath = "FeatBit:Authentication";

    public static FeatBitServiceOptions Apply(
        IConfiguration configuration,
        FeatBitServiceOptions api)
    {
        var environment = new Dictionary<string, string>(
            api.Environment, StringComparer.OrdinalIgnoreCase);
        var secrets = new Dictionary<string, string>(
            api.SecretParameters, StringComparer.OrdinalIgnoreCase);

        var ssoEnabled = configuration.GetValue<bool?>($"{ConfigurationPath}:SsoEnabled");
        if (ssoEnabled.HasValue)
        {
            if (environment.ContainsKey("SSOEnabled") || secrets.ContainsKey("SSOEnabled"))
            {
                throw new InvalidOperationException(
                    $"Use either {ConfigurationPath}:SsoEnabled or FeatBit:Api service " +
                    "configuration for SSOEnabled, not both.");
            }

            environment["SSOEnabled"] = ssoEnabled.Value ? "true" : "false";
        }

        var providers = new[] { "GitHub", "Google" }
            .Where(name => configuration.GetValue($"{ConfigurationPath}:{name}:Enabled", false))
            .ToArray();

        // Keep the native service configuration available for existing installations,
        // but do not combine two independently indexed OAuth provider arrays.
        if (providers.Length > 0 && environment.Keys.Concat(secrets.Keys).Any(
                name => name.StartsWith("OAuthProviders__", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Use either {ConfigurationPath} or FeatBit:Api service configuration " +
                "for OAuthProviders, not both. Remove the OAuthProviders__* entries " +
                "when enabling a named provider.");
        }

        for (var index = 0; index < providers.Length; index++)
        {
            var name = providers[index];
            var clientIdPath = $"{ConfigurationPath}:{name}:ClientId";
            var clientId = configuration[clientIdPath]?.Trim();
            if (string.IsNullOrEmpty(clientId))
            {
                throw new InvalidOperationException(
                    $"{clientIdPath} is required when {ConfigurationPath}:{name}:Enabled is true.");
            }

            var prefix = $"OAuthProviders__{index}";
            environment[$"{prefix}__Name"] = name;
            environment[$"{prefix}__ClientId"] = clientId;
            secrets[$"{prefix}__ClientSecret"] = $"api-{name.ToLowerInvariant()}-client-secret";
        }

        return new FeatBitServiceOptions(environment, secrets);
    }
}
