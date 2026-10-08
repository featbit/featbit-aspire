using System.Text.RegularExpressions;
using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Xunit;

namespace FeatBit.AppHost.Tests;

public sealed class AzureDeploymentRevisionTests
{
    [Theory]
    [InlineData("featbit-api")]
    [InlineData("featbit-evaluation")]
    [InlineData("featbit-ui")]
    public void ApplyingTheAppTemplateCreatesARevisionAndKeepsItsDomainBindings(string name)
    {
        var resource = CreateResource(name);
        var template = resource.GetBicepTemplateString().Replace("\r\n", "\n");

        Assert.Contains("param deploymentRevision string = newGuid()", template);
        Assert.Contains("revisionSuffix: 'r-${uniqueString(deploymentRevision)}'", template);
        Assert.Contains($"name: '{name}'", template);
        Assert.Contains("customDomains: existingCustomDomains", template);
        Assert.DoesNotContain("param existingCustomDomains array =", template);
        Assert.False(resource.Parameters.ContainsKey("deploymentRevision"));
    }

    [Theory]
    [InlineData("featbit-api")]
    [InlineData("featbit-evaluation")]
    [InlineData("featbit-ui")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void GeneratedRevisionNamesSatisfyAzureLengthAndCharacterConstraints(string name)
    {
        var template = CreateResource(name).GetBicepTemplateString();
        var suffix = Regex.Match(template,
            @"revisionSuffix: '(?<prefix>[^'$]*)\$\{uniqueString\(deploymentRevision\)\}'");
        Assert.True(suffix.Success, "The template must use a bounded revision identifier.");

        // Bicep uniqueString has a fixed 13-character result. Check the complete
        // Azure name; checking only the suffix missed the Evaluation failure.
        var revisionSuffix = suffix.Groups["prefix"].Value + new string('a', 13);
        Assert.Matches(@"^[a-z][a-z0-9-]*[a-z0-9]$", revisionSuffix);
        Assert.DoesNotContain("--", revisionSuffix);
        Assert.InRange($"{name}--{revisionSuffix}".Length, 1, 54);
    }

    [Fact]
    public void TemplateGenerationIsStableSoDeploymentCachingCanSkipUnchangedApps()
    {
        Assert.Equal(
            CreateResource("featbit-api").GetBicepTemplateString(),
            CreateResource("featbit-api").GetBicepTemplateString());
    }

    private static AzureProvisioningResource CreateResource(string name) =>
        new(name, infrastructure =>
        {
            var app = new ContainerApp("app")
            {
                Name = name,
                Template = new ContainerAppTemplate()
            };
            infrastructure.Add(app);
            FeatBitAzureExtensions.ConfigureDeploymentRevision(infrastructure, app);
            FeatBitAzureCustomDomains.ConfigureInfrastructure(infrastructure, app);
        });
}
