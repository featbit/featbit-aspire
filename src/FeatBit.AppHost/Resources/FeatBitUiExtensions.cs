using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public static class FeatBitUiExtensions
{
    public static IResourceBuilder<ContainerResource> AddFeatBitUi(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options,
        FeatBitServiceConfigurationResources serviceConfiguration,
        IResourceBuilder<ContainerResource> api,
        IResourceBuilder<ContainerResource> evaluation)
    {
        var apiUrl = GetBrowserUrl(api, options.IsPublishMode);
        var evaluationUrl = GetBrowserUrl(evaluation, options.IsPublishMode);

        return builder.AddContainer("featbit-ui", "featbit/featbit-ui", options.Version)
            .WithEnvironment("API_URL", apiUrl)
            .WithEnvironment("EVALUATION_URL", evaluationUrl)
            .WithHttpEndpoint(
                port: options.IsPublishMode ? null : 8081,
                targetPort: 80,
                name: "http")
            .WithExternalHttpEndpoints()
            .WaitFor(api)
            .WaitFor(evaluation)
            .WithFeatBitServiceConfiguration(
                FeatBitService.Ui,
                options.Ui,
                serviceConfiguration,
                options.Version)
            .PublishAsFeatBitAzureContainerApp(options, options.Azure.Ui);
    }

    private static ReferenceExpression GetBrowserUrl(
        IResourceBuilder<ContainerResource> resource,
        bool isPublishMode) =>
        isPublishMode
            ? ReferenceExpression.Create($"{resource.GetEndpoint("http")}")
            : ReferenceExpression.Create(
                $"http://localhost:{resource.GetEndpoint("http").Property(EndpointProperty.Port)}");
}
