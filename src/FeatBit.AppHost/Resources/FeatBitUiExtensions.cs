using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public static class FeatBitUiExtensions
{
    public static IResourceBuilder<ContainerResource> AddFeatBitUi(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options,
        IResourceBuilder<ContainerResource> api,
        IResourceBuilder<ContainerResource> evaluation)
    {
        var apiUrl = GetBrowserUrl(api, options.IsPublishMode);
        var evaluationUrl = GetBrowserUrl(evaluation, options.IsPublishMode);

        return builder.AddContainer("featbit-ui", "featbit/featbit-ui", options.Version)
            .WithEnvironment("API_URL", apiUrl)
            .WithEnvironment("EVALUATION_URL", evaluationUrl)
            .WithEnvironment("DEMO_URL", "https://featbit-samples.vercel.app")
            .WithEnvironment("BASE_HREF", "/")
            .WithHttpEndpoint(
                port: options.IsPublishMode ? null : 8081,
                targetPort: 80,
                name: "http")
            .WithExternalHttpEndpoints()
            .WaitFor(api)
            .WaitFor(evaluation)
            .PublishAsFeatBitAzureContainerApp(options);
    }

    private static ReferenceExpression GetBrowserUrl(
        IResourceBuilder<ContainerResource> resource,
        bool isPublishMode) =>
        isPublishMode
            ? ReferenceExpression.Create($"{resource.GetEndpoint("http")}")
            : ReferenceExpression.Create(
                $"http://localhost:{resource.GetEndpoint("http").Property(EndpointProperty.Port)}");
}
