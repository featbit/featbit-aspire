using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using FeatBit.AppHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FeatBit.AppHost.Tests;

public sealed class OpenTelemetryConfigurationTests
{
    [Theory]
    [InlineData(FeatBitService.Api, "featbit-api", 5000, "featbit-api")]
    [InlineData(FeatBitService.Els, "featbit-evaluation", 5100, "featbit-els")]
    public async Task AzureDashboardEnablesAllSignalsUsingThePlatformEndpointAtStartup(
        FeatBitService service, string name, int port, string serviceName)
    {
        var builder = CreateBuilder(new()
        {
            ["FeatBit:OpenTelemetry:Enabled"] = "true",
            ["FeatBit:OpenTelemetry:ExportTarget"] = "AzureDashboard",
            ["FeatBit:OpenTelemetry:UseHeaders"] = "true",
            ["FeatBit:OpenTelemetry:Insecure"] = "false",
            ["Parameters:otel-exporter-otlp-endpoint"] = "https://old-collector.example.com:4317"
        }, publish: true);
        var options = FeatBitOptions.Load(builder);
        var telemetry = builder.AddFeatBitOpenTelemetry(options);
        var backend = builder.AddFeatBitBackend(
            name, "test-image", port, serviceName, service,
            service == FeatBitService.Api ? options.Api : options.Els,
            builder.AddFeatBitServiceConfiguration(options), options,
            builder.AddFeatBitPostgres(options), builder.AddFeatBitRedis(options), telemetry);

        var environment = await GetEnvironment(builder, backend.Resource);

        Assert.Equal("true", environment["ENABLE_OPENTELEMETRY"]);
        Assert.Equal(serviceName, environment["OTEL_SERVICE_NAME"]);
        Assert.Equal("otlp", environment["OTEL_LOGS_EXPORTER"]);
        Assert.Equal("otlp", environment["OTEL_TRACES_EXPORTER"]);
        Assert.Equal("otlp", environment["OTEL_METRICS_EXPORTER"]);
        Assert.Equal("grpc", environment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.Equal("true", environment["OTEL_EXPORTER_OTLP_INSECURE"]);
        Assert.False(environment.ContainsKey("OTEL_EXPORTER_OTLP_ENDPOINT"));
        Assert.False(environment.ContainsKey("OTEL_EXPORTER_OTLP_HEADERS"));
        Assert.Null(telemetry.Endpoint);
        Assert.Null(telemetry.Headers);
        Assert.DoesNotContain(builder.Resources, resource => resource.Name.StartsWith("otel-exporter-"));
        Assert.Equal("/bin/sh", backend.Resource.Entrypoint);
        var arguments = await GetArguments(builder, backend.Resource);
        Assert.Equal(2, arguments.Count);
        Assert.Equal("-c", arguments[0]);
        var startupScript = Assert.IsType<string>(arguments[1]);
        Assert.DoesNotContain("\r", startupScript);
        Assert.Contains("CONTAINERAPP_OTEL_TRACING_GRPC_ENDPOINT", startupScript);
        Assert.Contains("export OTEL_EXPORTER_OTLP_ENDPOINT=", startupScript);
        Assert.Contains("exec ./start.sh", startupScript);
    }

    [Theory]
    [InlineData(false, "AzureDashboard")]
    [InlineData(false, "ExternalCollector")]
    [InlineData(true, "AzureDashboard")]
    [InlineData(true, "ExternalCollector")]
    public async Task DisabledTelemetryRequiresNoCollectorAndLeavesConsoleLoggingAvailable(
        bool publish, string target)
    {
        var builder = CreateBuilder(new()
        {
            ["FeatBit:OpenTelemetry:Enabled"] = "false",
            ["FeatBit:OpenTelemetry:ExportTarget"] = target,
            ["FeatBit:OpenTelemetry:UseHeaders"] = "true"
        }, publish);
        var options = FeatBitOptions.Load(builder);
        var telemetry = builder.AddFeatBitOpenTelemetry(options);
        var backend = builder.AddContainer("test-backend", "test-image")
            .WithFeatBitOpenTelemetry("test-backend", options, telemetry);

        var environment = await GetEnvironment(builder, backend.Resource);

        Assert.Equal("false", environment["ENABLE_OPENTELEMETRY"]);
        Assert.Single(environment);
        Assert.Null(telemetry.Endpoint);
        Assert.Null(telemetry.Headers);
        Assert.DoesNotContain(builder.Resources, resource => resource.Name.StartsWith("otel-exporter-"));
        Assert.Empty(backend.Resource.Annotations.OfType<OtlpExporterAnnotation>());
        Assert.Null(backend.Resource.Entrypoint);
        Assert.Empty(await GetArguments(builder, backend.Resource));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingPublishSettingsKeepExternalCollectorAndOptionalSecretHeaders(bool useHeaders)
    {
        var builder = CreateBuilder(new()
        {
            ["FeatBit:OpenTelemetry:Enabled"] = "true",
            ["FeatBit:OpenTelemetry:UseHeaders"] = useHeaders.ToString(),
            ["FeatBit:OpenTelemetry:Insecure"] = "false",
            ["Parameters:otel-exporter-otlp-endpoint"] = "https://collector.example.com:4317",
            ["Parameters:otel-exporter-otlp-headers"] = "authorization=test-value"
        }, publish: true);
        var options = FeatBitOptions.Load(builder);
        var telemetry = builder.AddFeatBitOpenTelemetry(options);
        var backend = builder.AddContainer("test-backend", "test-image")
            .WithFeatBitOpenTelemetry("test-backend", options, telemetry);

        var environment = await GetEnvironment(builder, backend.Resource);

        Assert.Equal(FeatBitOpenTelemetryExportTarget.ExternalCollector, options.OpenTelemetry.ExportTarget);
        Assert.Equal("https://collector.example.com:4317",
            await Assert.IsType<ReferenceExpression>(environment["OTEL_EXPORTER_OTLP_ENDPOINT"]).GetValueAsync(default));
        Assert.Equal("false", environment["OTEL_EXPORTER_OTLP_INSECURE"]);
        Assert.Null(backend.Resource.Entrypoint);
        Assert.Empty(await GetArguments(builder, backend.Resource));
        if (useHeaders)
        {
            Assert.Equal("authorization=test-value",
                await Assert.IsType<ReferenceExpression>(environment["OTEL_EXPORTER_OTLP_HEADERS"]).GetValueAsync(default));
            var headers = Assert.IsType<ParameterResource>(
                Assert.Single(builder.Resources, resource => resource.Name == "otel-exporter-otlp-headers"));
            Assert.True(headers.Secret);
        }
        else
        {
            Assert.False(environment.ContainsKey("OTEL_EXPORTER_OTLP_HEADERS"));
            Assert.DoesNotContain(builder.Resources, resource => resource.Name == "otel-exporter-otlp-headers");
        }
    }

    [Theory]
    [InlineData("AzureDashboard")]
    [InlineData("ExternalCollector")]
    public void LocalRunsAlwaysUseTheLocalAspireDashboard(string target)
    {
        var builder = CreateBuilder(new()
        {
            ["FeatBit:OpenTelemetry:Enabled"] = "true",
            ["FeatBit:OpenTelemetry:ExportTarget"] = target
        });
        var options = FeatBitOptions.Load(builder);
        var telemetry = builder.AddFeatBitOpenTelemetry(options);
        var backend = builder.AddContainer("test-backend", "test-image")
            .WithFeatBitOpenTelemetry("test-backend", options, telemetry);

        var exporter = Assert.Single(backend.Resource.Annotations.OfType<OtlpExporterAnnotation>());
        Assert.Equal(OtlpProtocol.Grpc, exporter.RequiredProtocol);
        Assert.Null(telemetry.Endpoint);
        Assert.Null(telemetry.Headers);
        Assert.DoesNotContain(builder.Resources, resource => resource.Name.StartsWith("otel-exporter-"));
        Assert.Null(backend.Resource.Entrypoint);
        Assert.Empty(backend.Resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>());
    }

    [Theory]
    [InlineData(" azuredashboard ", FeatBitOpenTelemetryExportTarget.AzureDashboard)]
    [InlineData("externalcollector", FeatBitOpenTelemetryExportTarget.ExternalCollector)]
    public void ExportTargetAcceptsCaseInsensitiveNames(string value, FeatBitOpenTelemetryExportTarget expected)
    {
        var builder = CreateBuilder(new() { ["FeatBit:OpenTelemetry:ExportTarget"] = value });

        Assert.Equal(expected, FeatBitOptions.Load(builder).OpenTelemetry.ExportTarget);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("0")]
    public void UnsupportedExportTargetFailsWithTheConfigurationKey(string value)
    {
        var builder = CreateBuilder(new() { ["FeatBit:OpenTelemetry:ExportTarget"] = value });

        var error = Assert.Throws<InvalidOperationException>(() => FeatBitOptions.Load(builder));

        Assert.Contains("FeatBit:OpenTelemetry:ExportTarget", error.Message);
        Assert.Contains("ExternalCollector and AzureDashboard", error.Message);
    }

    private static async Task<List<object>> GetArguments(
        IDistributedApplicationBuilder builder, ContainerResource resource)
    {
        var arguments = new List<object>();
        var context = new CommandLineArgsCallbackContext(arguments, resource, default)
        {
            ExecutionContext = builder.ExecutionContext
        };
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return arguments;
    }

    private static async Task<Dictionary<string, object>> GetEnvironment(
        IDistributedApplicationBuilder builder, ContainerResource resource)
    {
        var environment = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment, default);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return environment;
    }

    private static IDistributedApplicationBuilder CreateBuilder(
        Dictionary<string, string?>? configuration = null, bool publish = false)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "apphost.csproj")))
        {
            directory = directory.Parent;
        }

        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = publish ? ["--operation", "publish"] : [],
            AssemblyName = typeof(FeatBitOptions).Assembly.GetName().Name,
            ProjectDirectory = directory!.FullName,
            DisableDashboard = true
        });
        var appHostConfiguration = builder.Configuration.GetSection("AppHost")
            .AsEnumerable().ToDictionary(entry => entry.Key, entry => entry.Value);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(appHostConfiguration);
        builder.Configuration.AddInMemoryCollection(configuration ?? new());
        return builder;
    }
}
