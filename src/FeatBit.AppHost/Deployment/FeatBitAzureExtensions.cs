using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;

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

        return resource.PublishAsAzureContainerApp((infrastructure, app) =>
        {
            app.Template.Scale = new ContainerAppScale
            {
                MinReplicas = scale.MinReplicas,
                MaxReplicas = scale.MaxReplicas
            };
            configure?.Invoke(infrastructure, app);
        });
    }
}
