using FeatBit.AppHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FeatBit.AppHost.Tests;

public sealed class AuthenticationConfigurationTests
{
    [Theory]
    [InlineData(true, false, "GitHub")]
    [InlineData(false, true, "Google")]
    [InlineData(true, true, "GitHub", "Google")]
    public void EnabledProvidersHaveContiguousIndexesAndSecretReferences(
        bool github, bool google, params string[] names)
    {
        var result = Apply(new()
        {
            ["FeatBit:Authentication:GitHub:Enabled"] = github.ToString(),
            ["FeatBit:Authentication:GitHub:ClientId"] = " github-client-id ",
            ["FeatBit:Authentication:Google:Enabled"] = google.ToString(),
            ["FeatBit:Authentication:Google:ClientId"] = " google-client-id "
        });

        Assert.Equal(names.Length * 2, result.Environment.Count);
        Assert.Equal(names.Length, result.SecretParameters.Count);
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            var prefix = $"OAuthProviders__{index}";
            Assert.Equal(name, result.Environment[$"{prefix}__Name"]);
            Assert.Equal($"{name.ToLowerInvariant()}-client-id", result.Environment[$"{prefix}__ClientId"]);
            Assert.Equal($"api-{name.ToLowerInvariant()}-client-secret", result.SecretParameters[$"{prefix}__ClientSecret"]);
            Assert.False(result.Environment.ContainsKey($"{prefix}__ClientSecret"));
        }
    }

    [Fact]
    public void DisabledProvidersDoNotRequireCredentialsOrRegisterSecrets()
    {
        var result = Apply(new()
        {
            ["FeatBit:Authentication:GitHub:Enabled"] = "false",
            ["FeatBit:Authentication:Google:Enabled"] = "false"
        });

        Assert.Empty(result.Environment);
        Assert.Empty(result.SecretParameters);
    }

    [Theory]
    [InlineData("GitHub", null)]
    [InlineData("GitHub", " ")]
    [InlineData("Google", null)]
    [InlineData("Google", "")]
    public void EnabledProviderRequiresClientId(string provider, string? clientId)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Apply(new()
        {
            [$"FeatBit:Authentication:{provider}:Enabled"] = "true",
            [$"FeatBit:Authentication:{provider}:ClientId"] = clientId
        }));

        Assert.Contains($"FeatBit:Authentication:{provider}:ClientId", error.Message);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void SsoCanBeEnabledOrDisabledIndependently(string enabled)
    {
        var result = Apply(new() { ["FeatBit:Authentication:SsoEnabled"] = enabled });

        Assert.Equal(enabled, result.Environment["SSOEnabled"]);
        Assert.Single(result.Environment);
        Assert.Empty(result.SecretParameters);
    }

    [Fact]
    public void ExistingNativeConfigurationAndUnrelatedSettingsRemainSupported()
    {
        var api = new FeatBitServiceOptions(
            new Dictionary<string, string>
            {
                ["SSOEnabled"] = "true",
                ["OAuthProviders__0__Name"] = "Google",
                ["OAuthProviders__0__ClientId"] = "existing-client-id",
                ["Jwt__Issuer"] = "custom-issuer"
            },
            new Dictionary<string, string>
            {
                ["OAuthProviders__0__ClientSecret"] = "existing-secret"
            });

        var result = Apply(new(), api);

        Assert.Equal(api.Environment, result.Environment);
        Assert.Equal(api.SecretParameters, result.SecretParameters);
    }

    [Theory]
    [InlineData("oauthproviders__0__Name", false)]
    [InlineData("OAuthProviders__2__ClientSecret", true)]
    public void MixedProviderConfigurationIsRejected(string key, bool secret)
    {
        var api = NativeSetting(key, secret);
        var error = Assert.Throws<InvalidOperationException>(() => Apply(new()
        {
            ["FeatBit:Authentication:GitHub:Enabled"] = "true",
            ["FeatBit:Authentication:GitHub:ClientId"] = "client-id"
        }, api));

        Assert.Contains("OAuthProviders", error.Message);
        Assert.Contains("not both", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSsoConfigurationIsRejected(bool secret)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Apply(new()
        {
            ["FeatBit:Authentication:SsoEnabled"] = "true"
        }, NativeSetting("ssoenabled", secret)));

        Assert.Contains("SSOEnabled", error.Message);
    }

    [Fact]
    public void ApplyingAuthenticationPreservesUnrelatedSettingsWithoutMutatingInput()
    {
        var api = new FeatBitServiceOptions(
            new Dictionary<string, string> { ["Jwt__Issuer"] = "custom-issuer" },
            new Dictionary<string, string> { ["OtherSecret"] = "other-secret" });
        var result = Apply(new()
        {
            ["FeatBit:Authentication:SsoEnabled"] = "true",
            ["FeatBit:Authentication:Google:Enabled"] = "true",
            ["FeatBit:Authentication:Google:ClientId"] = "client-id"
        }, api);

        Assert.Equal("custom-issuer", result.Environment["Jwt__Issuer"]);
        Assert.Equal("other-secret", result.SecretParameters["OtherSecret"]);
        Assert.Single(api.Environment);
        Assert.Single(api.SecretParameters);
    }

    private static FeatBitServiceOptions NativeSetting(string key, bool secret) => new(
        secret ? new Dictionary<string, string>() : new Dictionary<string, string> { [key] = "value" },
        secret ? new Dictionary<string, string> { [key] = "existing-secret" } : new Dictionary<string, string>());

    private static FeatBitServiceOptions Apply(
        Dictionary<string, string?> values,
        FeatBitServiceOptions? api = null) => FeatBitAuthenticationConfiguration.Apply(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            api ?? new FeatBitServiceOptions(new Dictionary<string, string>(), new Dictionary<string, string>()));
}
