using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Azure;
using Azure.Provisioning.AppContainers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FeatBit.AppHost.Tests;

#pragma warning disable ASPIREPIPELINES001
public sealed class AzureCustomDomainPreservationTests
{
    private const string ConfiguredSubscription = "11111111-1111-1111-1111-111111111111";
    private const string SelectedSubscription = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task PreservesAllAzureBindingsIncludingPendingAndWildcardDomains()
    {
        const string json = """
            {"configuration":{"ingress":{"customDomains":[
              {"name":"app.example.com","certificateId":"/managedCertificates/current-cert","bindingType":"SniEnabled"},
              {"name":"*.example.org","certificateId":"/certificates/uploaded-cert","bindingType":"SniEnabled"},
              {"name":"pending.example.com","bindingType":"Disabled","certificateId":null}
            ]}}}
            """;
        var actual = await Read(json);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json)!["configuration"]!["ingress"]!["customDomains"], actual));
    }

    [Theory]
    [InlineData("{\"configuration\":{\"ingress\":{\"customDomains\":null}}}")]
    [InlineData("{\"configuration\":{\"ingress\":{\"customDomains\":[]}}}")]
    [InlineData("{\"configuration\":{\"ingress\":null}}")]
    public async Task ExistingAppWithoutBindingsKeepsAnEmptyList(string json) => Assert.Empty(await Read(json));

    [Fact]
    public async Task FirstDeploymentUsesEmptyBindingsOnlyForConfirmedMissingApp()
    {
        var bindings = await FeatBitAzureCustomDomains.ReadAsync(_ =>
            throw new RequestFailedException(404, "Missing app", "ResourceNotFound", null));
        Assert.Empty(bindings);
    }

    [Theory]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "AuthorizationFailed")]
    [InlineData(404, "ResourceGroupNotFound")]
    [InlineData(429, "TooManyRequests")]
    [InlineData(500, "InternalServerError")]
    public async Task AzureLookupFailureAbortsInsteadOfReturningEmptyBindings(int status, string code)
    {
        var error = new RequestFailedException(status, "Lookup failed", code, null);
        Assert.Same(error, await Assert.ThrowsAsync<RequestFailedException>(() =>
            FeatBitAzureCustomDomains.ReadAsync(_ => throw error)));
    }

    [Fact]
    public async Task TimeoutAndCancellationDoNotBecomeEmptyBindings()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            FeatBitAzureCustomDomains.ReadAsync(_ => throw new HttpRequestException()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FeatBitAzureCustomDomains.ReadAsync(token => Task.FromCanceled<BinaryData?>(token), cancellation.Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"configuration\":null}")]
    [InlineData("{\"configuration\":{\"ingress\":{\"customDomains\":{}}}}")]
    public async Task MalformedAzureConfigurationStopsDeployment(string json) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Read(json));

    [Fact]
    public async Task ANewReadUsesCurrentBindingsIncludingPortalRemovals()
    {
        Assert.Single(await Read("""{"configuration":{"ingress":{"customDomains":[{"name":"app.example.com","bindingType":"Disabled"}]}}}"""));
        Assert.Empty(await Read("""{"configuration":{"ingress":{"customDomains":[]}}}"""));
    }

    [Fact]
    public void TemplateRequiresBindingsAndRegenerationDoesNotEraseTheSnapshot()
    {
        var resource = new AzureProvisioningResource("test-app", infrastructure =>
        {
            var app = new ContainerApp("app") { Name = "test-app" };
            infrastructure.Add(app);
            FeatBitAzureCustomDomains.ConfigureInfrastructure(infrastructure, app);
        });
        var template = resource.GetBicepTemplateString();
        Assert.Contains("param existingCustomDomains array\n", template.Replace("\r\n", "\n"));
        Assert.Contains("customDomains: existingCustomDomains", template);
        Assert.DoesNotContain("param existingCustomDomains array =", template);

        Func<object?> snapshot = () => JsonNode.Parse("""[{"name":"app.example.com","bindingType":"SniEnabled","certificateId":"/existing-cert"}]""");
        resource.Parameters[FeatBitAzureCustomDomains.ParameterName] = snapshot;
        using var regenerated = resource.GetBicepTemplateFile();
        Assert.Same(snapshot, resource.Parameters[FeatBitAzureCustomDomains.ParameterName]);
    }

    [Theory]
    [InlineData("featbit-ui")]
    [InlineData("featbit-api")]
    [InlineData("featbit-evaluation")]
    public void ContainerAppProvisioningRequiresItsDomainLookup(string name)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var container = builder.AddContainer(name, "test-image").WithPreservedAzureCustomDomains();
        var target = new AzureProvisioningResource($"{name}-containerapp", _ => { });
        container.Resource.Annotations.Add(new DeploymentTargetAnnotation(target));
        var provision = new PipelineStep
        {
            Name = $"provision-{target.Name}",
            Resource = target,
            Tags = [WellKnownPipelineTags.ProvisionInfrastructure],
            Action = _ => Task.CompletedTask
        };
        var unrelated = new PipelineStep
        {
            Name = "unrelated",
            Resource = new AzureProvisioningResource("other-app", _ => { }),
            Tags = [WellKnownPipelineTags.ProvisionInfrastructure],
            Action = _ => Task.CompletedTask
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new PipelineConfigurationContext
        {
            Services = services,
            Model = new DistributedApplicationModel(builder.Resources),
            Steps = [provision, unrelated]
        };
        foreach (var annotation in container.Resource.Annotations.OfType<PipelineConfigurationAnnotation>())
        {
            annotation.Callback(context);
        }

        Assert.Contains($"preserve-custom-domains-{name}", provision.DependsOnSteps);
        Assert.Empty(unrelated.DependsOnSteps);
    }

    [Theory]
    [InlineData("featbit-ui")]
    [InlineData("featbit-api")]
    [InlineData("featbit-evaluation")]
    public void ClearCacheUsesConfiguredTargetWhenNoAzureStateWasSaved(string name)
    {
        var configuration = AzureConfiguration(ConfiguredSubscription, "configured-group");
        var id = FeatBitAzureCustomDomains.ResolveContainerAppId(new JsonObject(), configuration, name);

        Assert.Equal(ConfiguredSubscription, id.SubscriptionId);
        Assert.Equal("configured-group", id.ResourceGroupName);
        Assert.Equal(name, id.Name);
        Assert.Equal("Microsoft.App/containerApps", id.ResourceType.ToString());
    }

    [Fact]
    public void SavedProvisioningTargetTakesPrecedenceOverConfiguredDefaults()
    {
        var state = new JsonObject
        {
            ["SubscriptionId"] = SelectedSubscription,
            ["ResourceGroup"] = "selected-group"
        };
        var configuration = AzureConfiguration(ConfiguredSubscription, "configured-group");
        var id = FeatBitAzureCustomDomains.ResolveContainerAppId(state, configuration, "featbit-ui");

        Assert.Equal(SelectedSubscription, id.SubscriptionId);
        Assert.Equal("selected-group", id.ResourceGroupName);
    }

    [Theory]
    [InlineData("SubscriptionId", "ResourceGroup")]
    [InlineData("ResourceGroup", "SubscriptionId")]
    public void PartialStateDoesNotCombineDifferentDeploymentTargets(string present, string missing)
    {
        var state = new JsonObject { [present] = "selected-value" };
        var configuration = AzureConfiguration(ConfiguredSubscription, "configured-group");

        var error = Assert.Throws<InvalidOperationException>(() =>
            FeatBitAzureCustomDomains.ResolveContainerAppId(state, configuration, "featbit-ui"));

        Assert.Contains($"missing {missing}", error.Message);
    }

    [Theory]
    [InlineData(null, "configured-group", "SubscriptionId")]
    [InlineData(" ", "configured-group", "SubscriptionId")]
    [InlineData("configured-subscription", null, "ResourceGroup")]
    [InlineData("configured-subscription", " ", "ResourceGroup")]
    public void MissingClearCacheTargetStopsDomainLookup(string? subscription, string? group, string missing)
    {
        var configuration = AzureConfiguration(subscription, group);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FeatBitAzureCustomDomains.ResolveContainerAppId(new JsonObject(), configuration, "featbit-ui"));

        Assert.Contains($"Azure:{missing}", error.Message);
        Assert.Contains("--clear-cache", error.Message);
    }

    private static IConfiguration AzureConfiguration(string? subscription, string? group) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Azure:SubscriptionId"] = subscription,
            ["Azure:ResourceGroup"] = group
        }).Build();

    private static Task<JsonArray> Read(string json) =>
        FeatBitAzureCustomDomains.ReadAsync(_ => Task.FromResult<BinaryData?>(BinaryData.FromString(json)));
}
#pragma warning restore ASPIREPIPELINES001
