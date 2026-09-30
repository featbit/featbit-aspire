using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Core;
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
        FeatBitAzureContainerAppOptions containerAppOptions,
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
                MinReplicas = containerAppOptions.MinReplicas,
                MaxReplicas = containerAppOptions.MaxReplicas
            };
            foreach (var domain in containerAppOptions.CustomDomains)
            {
                // Reapply existing bindings on every deployment: ARM updates do
                // not preserve custom domains configured only in the Portal.
                app.Configuration.Ingress.CustomDomains.Add(new ContainerAppCustomDomain
                {
                    Name = domain.Name,
                    CertificateId = new ResourceIdentifier(domain.CertificateId),
                    BindingType = ContainerAppCustomDomainBindingType.SniEnabled
                });
            }
            configure?.Invoke(infrastructure, app);
        });
    }
}
