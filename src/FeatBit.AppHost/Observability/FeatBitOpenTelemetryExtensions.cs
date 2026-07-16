using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitOpenTelemetryResources(
    ReferenceExpression? Endpoint,
    ReferenceExpression? Headers);

public static class FeatBitOpenTelemetryExtensions
{
    public static FeatBitOpenTelemetryResources AddFeatBitOpenTelemetry(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (!options.IsPublishMode || !options.OpenTelemetry.Enabled)
        {
            return new FeatBitOpenTelemetryResources(null, null);
        }

        var endpoint = builder.AddParameter("otel-exporter-otlp-endpoint")
            .WithDescription(
                "External OpenTelemetry Collector OTLP/gRPC endpoint used by the Azure deployment.");
        ReferenceExpression? headers = null;

        if (options.OpenTelemetry.UseHeaders)
        {
            var headerParameter = builder.AddParameter(
                    "otel-exporter-otlp-headers",
                    secret: true)
                .WithDescription(
                    "Optional secret OTLP exporter headers, for example authorization metadata.");
            headers = ReferenceExpression.Create($"{headerParameter}");
        }

        return new FeatBitOpenTelemetryResources(
            ReferenceExpression.Create($"{endpoint}"),
            headers);
    }

    public static IResourceBuilder<ContainerResource> WithFeatBitOpenTelemetry(
        this IResourceBuilder<ContainerResource> resource,
        string serviceName,
        FeatBitOptions options,
        FeatBitOpenTelemetryResources telemetry)
    {
        resource.WithEnvironment(
            "ENABLE_OPENTELEMETRY",
            options.OpenTelemetry.Enabled ? "true" : "false");

        if (!options.OpenTelemetry.Enabled)
        {
            return resource;
        }

        if (options.IsPublishMode)
        {
            resource.WithEnvironment(
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                telemetry.Endpoint ?? throw new InvalidOperationException(
                    "An OTLP endpoint is required when OpenTelemetry is enabled in publish mode."));
        }
        else
        {
            resource.WithOtlpExporter(OtlpProtocol.Grpc);
        }

        resource
            .WithEnvironment("OTEL_SERVICE_NAME", serviceName)
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment(
                "OTEL_EXPORTER_OTLP_INSECURE",
                options.OpenTelemetry.Insecure ? "true" : "false");

        if (telemetry.Headers is not null)
        {
            resource.WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", telemetry.Headers);
        }

        return resource;
    }
}
