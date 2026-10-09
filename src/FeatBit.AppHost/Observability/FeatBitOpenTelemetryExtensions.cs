using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitOpenTelemetryResources(
    ReferenceExpression? Endpoint,
    ReferenceExpression? Headers);

public static class FeatBitOpenTelemetryExtensions
{
    private const string AzureDashboardStartupScript = """
        if [ -n "${CONTAINERAPP_OTEL_TRACING_GRPC_ENDPOINT:-}" ]; then
            aca_otlp_endpoint="${CONTAINERAPP_OTEL_TRACING_GRPC_ENDPOINT%/}"
            export OTEL_EXPORTER_OTLP_ENDPOINT="${aca_otlp_endpoint%/v1/traces}"
        else
            printf '%s\n' 'FeatBit: ACA did not inject its OpenTelemetry endpoint; telemetry cannot reach the Aspire dashboard.' >&2
        fi
        exec ./start.sh
        """;

    public static FeatBitOpenTelemetryResources AddFeatBitOpenTelemetry(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (!options.IsPublishMode || !options.OpenTelemetry.Enabled ||
            options.OpenTelemetry.ExportTarget == FeatBitOpenTelemetryExportTarget.AzureDashboard)
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

        var useAzureDashboard = options.IsPublishMode &&
            options.OpenTelemetry.ExportTarget == FeatBitOpenTelemetryExportTarget.AzureDashboard;

        if (options.IsPublishMode && !useAzureDashboard)
        {
            resource.WithEnvironment(
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                telemetry.Endpoint ?? throw new InvalidOperationException(
                    "An OTLP endpoint is required when OpenTelemetry exports to ExternalCollector in publish mode."));
        }
        else if (!options.IsPublishMode)
        {
            resource.WithOtlpExporter(OtlpProtocol.Grpc);
        }

        if (useAzureDashboard)
        {
            // FeatBit images default the OTLP endpoint to localhost. Resolve ACA's
            // runtime endpoint before the image's start.sh configures all exporters.
            // Linux shells need LF even when the AppHost source was checked out with CRLF.
            resource.WithEntrypoint("/bin/sh")
                .WithArgs("-c", AzureDashboardStartupScript.ReplaceLineEndings("\n"));
        }

        resource
            .WithEnvironment("OTEL_SERVICE_NAME", serviceName)
            .WithEnvironment("OTEL_LOGS_EXPORTER", "otlp")
            .WithEnvironment("OTEL_TRACES_EXPORTER", "otlp")
            .WithEnvironment("OTEL_METRICS_EXPORTER", "otlp")
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment(
                "OTEL_EXPORTER_OTLP_INSECURE",
                useAzureDashboard || options.OpenTelemetry.Insecure ? "true" : "false");

        if (!useAzureDashboard && telemetry.Headers is not null)
        {
            resource.WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", telemetry.Headers);
        }

        return resource;
    }
}
