using Microsoft.Extensions.Configuration;
using Xunit;

namespace FeatBit.AppHost.Tests;

public sealed class AzureCustomDomainTests
{
    private const string CertificateId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.App/managedEnvironments/test/managedCertificates/test-cert";
    private const string ConfigurationPath = "FeatBit:Azure:Ui:CustomDomains";

    [Fact]
    public void DomainsAreOptionalForExistingConfigurations() => Assert.Empty(Load(new()));

    [Theory]
    [InlineData("managedCertificates")]
    [InlineData("certificates")]
    public void ExistingManagedAndUploadedCertificatesAreSupported(string certificateType)
    {
        var id = CertificateId.Replace("managedCertificates", certificateType);
        var domains = Load(new()
        {
            [$"{ConfigurationPath}:0:Name"] = " app.example.com ",
            [$"{ConfigurationPath}:0:CertificateId"] = $" {id} "
        });

        var domain = Assert.Single(domains);
        Assert.Equal("app.example.com", domain.Name);
        Assert.Equal(id, domain.CertificateId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://app.example.com")]
    [InlineData("app.example.com:443")]
    [InlineData("app.example.com/path")]
    public void InvalidHostnameFailsBeforeDeployment(string? name)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(new()
        {
            [$"{ConfigurationPath}:0:Name"] = name,
            [$"{ConfigurationPath}:0:CertificateId"] = CertificateId
        }));

        Assert.Contains($"{ConfigurationPath}:0:Name", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test-cert")]
    [InlineData("https://example.com/certificate")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/test/providers/Microsoft.KeyVault/vaults/test/secrets/test-cert")]
    public void InvalidCertificateFailsBeforeDeployment(string? certificateId)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(new()
        {
            [$"{ConfigurationPath}:0:Name"] = "app.example.com",
            [$"{ConfigurationPath}:0:CertificateId"] = certificateId
        }));

        Assert.Contains($"{ConfigurationPath}:0:CertificateId", error.Message);
    }

    [Fact]
    public void DuplicateHostnamesAreRejectedIgnoringCase()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(new()
        {
            [$"{ConfigurationPath}:0:Name"] = "app.example.com",
            [$"{ConfigurationPath}:0:CertificateId"] = CertificateId,
            [$"{ConfigurationPath}:1:Name"] = "APP.example.com",
            [$"{ConfigurationPath}:1:CertificateId"] = CertificateId
        }));

        Assert.Contains("duplicate hostname", error.Message);
    }

    [Fact]
    public void MultipleBindingsAndWildcardDomainsArePreserved()
    {
        var domains = Load(new()
        {
            [$"{ConfigurationPath}:0:Name"] = "app.example.com",
            [$"{ConfigurationPath}:0:CertificateId"] = CertificateId,
            [$"{ConfigurationPath}:1:Name"] = "*.example.org",
            [$"{ConfigurationPath}:1:CertificateId"] = CertificateId
        });

        Assert.Equal(new[] { "app.example.com", "*.example.org" }, domains.Select(domain => domain.Name));
    }

    private static IReadOnlyList<FeatBitAzureCustomDomainOptions> Load(Dictionary<string, string?> values) =>
        FeatBitAzureCustomDomainOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values).Build().GetSection(ConfigurationPath));
}
