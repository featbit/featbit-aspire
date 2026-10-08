using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;

namespace FeatBit.AppHost;

public static class FeatBitAzureExtensions
{
    public static void AddFeatBitAzureEnvironment(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (options.IsPublishMode)
        {
            builder.AddAzureContainerAppEnvironment("featbit-aca");
        }
    }

    public static IResourceBuilder<ContainerResource> PublishAsFeatBitAzureContainerApp(
        this IResourceBuilder<ContainerResource> resource,
        FeatBitOptions options,
        FeatBitAzureScaleOptions scale,
        Action<AzureResourceInfrastructure, ContainerApp>? configure = null)
    {
        if (!options.IsPublishMode)
        {
            return resource;
        }

        return resource.WithPreservedAzureCustomDomains().PublishAsAzureContainerApp((infrastructure, app) =>
        {
            app.Name = resource.Resource.Name;
            app.Template.Scale = new ContainerAppScale
            {
                MinReplicas = scale.MinReplicas,
                MaxReplicas = scale.MaxReplicas
            };
            configure?.Invoke(infrastructure, app);
            ConfigureDeploymentRevision(infrastructure, app);
            FeatBitAzureCustomDomains.ConfigureInfrastructure(infrastructure, app);
        });
    }

    public static void ConfigureDeploymentRevision(AzureResourceInfrastructure infrastructure, ContainerApp app)
    {
        // ACA secret updates alone leave existing replicas on their old values.
        // Generate a revision when ARM applies this app template, without making
        // the generated template itself change on every AppHost build.
        var revision = new ProvisioningParameter("deploymentRevision", typeof(string))
        {
            Description = "Unique revision for this Container App deployment so updated secrets take effect.",
            Value = new FunctionCallExpression(new IdentifierExpression("newGuid"), [])
        };
        infrastructure.Add(revision);
        // uniqueString returns 13 lowercase alphanumeric characters. The r-
        // prefix makes a valid suffix and keeps the full revision name under
        // ACA's 54-character limit, including the Container App name and --.
        var revisionId = BicepFunction.GetUniqueString(revision);
        app.Template.RevisionSuffix = BicepFunction.Interpolate($"r-{revisionId}");
    }
}
