using FeatBit.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var options = FeatBitOptions.Load(builder);

builder.AddFeatBitAzureEnvironment(options);
var serviceConfiguration = builder.AddFeatBitServiceConfiguration(options);

var postgres = builder.AddFeatBitPostgres(options);
var redis = builder.AddFeatBitRedis(options);
var telemetry = builder.AddFeatBitOpenTelemetry(options);
var jwt = builder.AddFeatBitJwt(options);

var api = builder.AddFeatBitBackend(
        name: "featbit-api",
        image: "featbit/featbit-api-server",
        port: 5000,
        telemetryServiceName: "featbit-api",
        service: FeatBitService.Api,
        serviceOptions: options.Api,
        serviceConfiguration,
        options,
        postgres,
        redis,
        telemetry)
    .WithFeatBitJwt(options, jwt)
    .PublishAsFeatBitAzureContainerApp(
        options,
        options.Azure.Api,
        (infrastructure, app) =>
            jwt.ConfigureAzureContainerApp(infrastructure, app, options));

var evaluation = builder.AddFeatBitBackend(
        name: "featbit-evaluation",
        image: "featbit/featbit-evaluation-server",
        port: 5100,
        telemetryServiceName: "featbit-els",
        service: FeatBitService.Els,
        serviceOptions: options.Els,
        serviceConfiguration,
        options,
        postgres,
        redis,
        telemetry)
    .PublishAsFeatBitAzureContainerApp(options, options.Azure.Els);

builder.AddFeatBitUi(options, serviceConfiguration, api, evaluation);

builder.Build().Run();
