using FeatBit.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var options = FeatBitOptions.Load(builder);

builder.AddFeatBitAzureEnvironment(options);

var postgres = await builder.AddFeatBitPostgresAsync(options);
var redis = builder.AddFeatBitRedis(options);
var telemetry = builder.AddFeatBitOpenTelemetry(options);
var jwt = builder.AddFeatBitJwt(options);

var api = builder.AddFeatBitBackend(
        name: "featbit-api",
        image: "featbit/featbit-api-server",
        port: 5000,
        telemetryServiceName: "featbit-api",
        options,
        postgres,
        redis,
        telemetry)
    .WithFeatBitJwt(options, jwt)
    .PublishAsFeatBitAzureContainerApp(
        options,
        (infrastructure, app) =>
            jwt.ConfigureAzureContainerApp(infrastructure, app, options));

var evaluation = builder.AddFeatBitBackend(
        name: "featbit-evaluation",
        image: "featbit/featbit-evaluation-server",
        port: 5100,
        telemetryServiceName: "featbit-els",
        options,
        postgres,
        redis,
        telemetry)
    .PublishAsFeatBitAzureContainerApp(options);

builder.AddFeatBitUi(options, api, evaluation);

builder.Build().Run();
