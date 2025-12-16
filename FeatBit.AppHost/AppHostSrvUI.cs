namespace FeatBit.AppHost;

public static class AppHostSrvUI
{
    public static IResourceBuilder<ContainerResource> AddUIService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> webApi,
        IResourceBuilder<ContainerResource> evaluationServer)
    {
        // FeatBit Angular UI - wait for webapi and evaluation server to be ready
        var angularUIBuilder = builder.AddContainer("featbit-ui", "featbit/featbit-ui", "latest")
            .WaitFor(webApi)
            .WaitFor(evaluationServer);

        if (builder.ExecutionContext.IsPublishMode)
        {
            angularUIBuilder = angularUIBuilder
                .WithHttpEndpoint(targetPort: 80, name: "http");
        }
        else
        {
            angularUIBuilder = angularUIBuilder
                .WithHttpEndpoint(port: 8081, targetPort: 80, name: "http");
        }

        var angularUI = angularUIBuilder
            .WithEnvironment("DEMO_URL", "https://featbit-samples.vercel.app");

        // Configure API and Evaluation URLs for browser access (external URLs)
        if (builder.ExecutionContext.IsPublishMode)
        {
            // Important: Since the UI runs in the browser, it needs external URLs
            // The webapi and evaluation server must have External = true in their ingress config
            angularUI = angularUI
                .WithEnvironment("API_URL", webApi.GetEndpoint("https"))
                .WithEnvironment("EVALUATION_URL", evaluationServer.GetEndpoint("https"))
                .WithExternalHttpEndpoints()
                .PublishAsAzureContainerApp((infrastructure, containerApp) =>
                {
                    containerApp.Template.Scale.MinReplicas = 3;
                    containerApp.Template.Scale.MaxReplicas = 10;
                    containerApp.Configuration.Ingress.External = true;
                });
        }
        // For development, manually specify the localhost URLs that will be accessible from browser
        else
        {
            angularUI = angularUI
                .WithEnvironment("API_URL", "http://localhost:5000")
                .WithEnvironment("EVALUATION_URL", "http://localhost:5100")
                .WithExternalHttpEndpoints();
        }

        return angularUI;
    }
}
