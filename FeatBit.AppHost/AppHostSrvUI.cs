namespace FeatBit.AppHost;

public static class AppHostSrvUI
{
    public static IResourceBuilder<ContainerResource> AddUIService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> webApi,
        IResourceBuilder<ContainerResource> evaluationServer)
    {
        // FeatBit Angular UI
        var angularUIBuilder = builder.AddContainer("featbit-ui", "featbit/featbit-ui", "latest");
        
        if (builder.ExecutionContext.IsPublishMode)
        {
            angularUIBuilder = angularUIBuilder
                .WithHttpEndpoint(name: "http");
        }
        else
        {
            angularUIBuilder = angularUIBuilder
                .WithHttpEndpoint(port: 8081, targetPort: 80, name: "http");
        }

        var angularUI = angularUIBuilder
            .WithEnvironment("DEMO_URL", "https://featbit-samples.vercel.app")
            .PublishAsAzureContainerApp((infrastructure, containerApp) =>
            {
                // Set minimum replicas to 3 for high availability
                containerApp.Template.Scale.MinReplicas = 0;
                // Optionally set maximum replicas
                containerApp.Template.Scale.MaxReplicas = 10;
            });

        // Configure API and Evaluation URLs for browser access (external URLs)
        // In production, these should be replaced with actual external URLs
        if (builder.ExecutionContext.IsPublishMode)
        {
            angularUI = angularUI
                .WithEnvironment("API_URL", webApi.GetEndpoint("http"))
                .WithEnvironment("EVALUATION_URL", evaluationServer.GetEndpoint("http"))
                .WithExternalHttpEndpoints();
        }
        // For development, manually specify the localhost URLs that will be accessible from browser
        else
        {
            angularUI = angularUI
                .WithEnvironment("API_URL", "http://localhost:5000")
                .WithEnvironment("EVALUATION_URL", "http://localhost:5100");
        }

        return angularUI;
    }
}
