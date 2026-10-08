using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Azure;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FeatBit.AppHost;

// Aspire 13.6 exposes deployment state and pipeline tags as preview APIs.
#pragma warning disable ASPIREPIPELINES001, ASPIREPIPELINES002
public static class FeatBitAzureCustomDomains
{
    public const string ParameterName = "existingCustomDomains";

    public static void ConfigureInfrastructure(AzureResourceInfrastructure infrastructure, ContainerApp app)
    {
        // No empty default: deploying this template without reading the current
        // bindings must fail instead of silently removing live domains.
        var domains = new ProvisioningParameter(ParameterName, typeof(object[]))
        {
            Description = "Current Azure custom domain bindings, read immediately before deployment."
        };
        infrastructure.Add(domains);
        app.Configuration.Ingress.CustomDomains = domains;
        infrastructure.AspireResource.Parameters.TryAdd(ParameterName, null);
    }

    public static IResourceBuilder<ContainerResource> WithPreservedAzureCustomDomains(
        this IResourceBuilder<ContainerResource> resource)
    {
        var stepName = $"preserve-custom-domains-{resource.Resource.Name}";

        return resource.WithPipelineStepFactory(
            stepName,
            async context =>
            {
                var target = GetDeploymentTarget(resource.Resource);
                var state = await context.Services.GetRequiredService<IDeploymentStateManager>()
                    .AcquireCurrentSectionAsync("Azure", context.CancellationToken);
                var configuration = context.Services.GetRequiredService<IConfiguration>();
                var id = ResolveContainerAppId(state.Data, configuration, resource.Resource.Name);
                var credential = context.Services.GetRequiredService<ITokenCredentialProvider>().TokenCredential;
                var clientOptions = new ArmClientOptions();
                clientOptions.SetApiVersion(new ResourceType("Microsoft.App/containerApps"), "2025-01-01");
                var client = new ArmClient(credential, id.SubscriptionId, clientOptions);

                var domains = await ReadAsync(async cancellationToken =>
                {
                    var response = await client.GetGenericResource(id).GetAsync(cancellationToken);
                    return response.Value.Data.Properties;
                }, context.CancellationToken);

                // Aspire can serialize parameters more than once. Return a new
                // JSON tree each time because JsonNode can have only one parent.
                target.Parameters[ParameterName] = (Func<object?>)(() => domains.DeepClone());
                context.Logger.LogInformation("Preserving {Count} custom domain binding(s) for {AppName}.",
                    domains.Count, resource.Resource.Name);
            },
            dependsOn: ["create-provisioning-context"],
            description: $"Reads existing custom domains before updating {resource.Resource.Name}.")
            .WithPipelineConfiguration(context =>
            {
                // Deployment targets are materialized during BeforeStart. This
                // callback also runs before then, when there is nothing to wire.
                if (resource.Resource.GetDeploymentTargetAnnotation()?.DeploymentTarget is AzureBicepResource target)
                {
                    context.GetSteps(target, WellKnownPipelineTags.ProvisionInfrastructure).DependsOn(stepName);
                }
            });
    }

    public static async Task<JsonArray> ReadAsync(
        Func<CancellationToken, Task<BinaryData?>> readProperties,
        CancellationToken cancellationToken = default)
    {
        BinaryData? properties;
        try
        {
            properties = await readProperties(cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "ResourceNotFound")
        {
            // Only a confirmed missing app is a first deployment. Authentication,
            // network and other Azure errors must abort the dependent update.
            return [];
        }

        if (properties is null || JsonNode.Parse(properties.ToString()) is not JsonObject root ||
            root["configuration"] is not JsonObject configuration)
        {
            throw new InvalidOperationException("Azure returned invalid Container App configuration; deployment stopped to preserve custom domains.");
        }

        var domains = configuration["ingress"]?["customDomains"];
        return domains switch
        {
            null => [],
            JsonArray bindings => (JsonArray)bindings.DeepClone(),
            _ => throw new InvalidOperationException("Azure returned invalid custom domain bindings; deployment stopped.")
        };
    }

    private static AzureBicepResource GetDeploymentTarget(IResource resource) =>
        resource.GetDeploymentTargetAnnotation()?.DeploymentTarget as AzureBicepResource
        ?? throw new InvalidOperationException($"The Azure deployment target for '{resource.Name}' is not available.");

    public static ResourceIdentifier ResolveContainerAppId(
        JsonObject state,
        IConfiguration configuration,
        string appName)
    {
        var subscription = state["SubscriptionId"]?.GetValue<string>();
        var resourceGroup = state["ResourceGroup"]?.GetValue<string>();

        // --clear-cache skips state saves, including the provisioning target.
        // Use the effective AppHost configuration when no target was saved.
        // Keep each subscription/resource-group pair from the same source.
        if (string.IsNullOrWhiteSpace(subscription) && string.IsNullOrWhiteSpace(resourceGroup))
        {
            subscription = configuration["Azure:SubscriptionId"];
            resourceGroup = configuration["Azure:ResourceGroup"];
        }

        return new ResourceIdentifier(
            $"/subscriptions/{GetRequiredSetting(subscription, "SubscriptionId")}" +
            $"/resourceGroups/{GetRequiredSetting(resourceGroup, "ResourceGroup")}" +
            $"/providers/Microsoft.App/containerApps/{appName}");
    }

    private static string GetRequiredSetting(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value
        : throw new InvalidOperationException(
            $"Azure deployment target is missing {name}; set Azure:{name} in AppHost configuration " +
            "when deploying with --clear-cache. Custom domain lookup cannot proceed.");
}
#pragma warning restore ASPIREPIPELINES001, ASPIREPIPELINES002
