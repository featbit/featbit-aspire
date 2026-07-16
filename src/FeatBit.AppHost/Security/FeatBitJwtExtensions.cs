using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace FeatBit.AppHost;

public sealed record FeatBitJwtResources(
    IResourceBuilder<ParameterResource>? SymmetricKey,
    IResourceBuilder<ParameterResource>? PrivateKey,
    IResourceBuilder<ParameterResource>? PublicKey);

public static class FeatBitJwtExtensions
{
    public const string PrivateKeyContainerPath = "/app/secrets/jwt/private.pem";
    public const string PublicKeyContainerPath = "/app/secrets/jwt/public.pem";

    public static FeatBitJwtResources AddFeatBitJwt(
        this IDistributedApplicationBuilder builder,
        FeatBitOptions options)
    {
        if (options.Jwt.Algorithm == FeatBitJwtAlgorithm.HS256)
        {
            var key = builder.AddParameter(
                    "jwt-key",
                    new GenerateParameterDefault { MinLength = 64 },
                    secret: true,
                    persist: true)
                .WithDescription("Auto-generated HS256 signing key for FeatBit API access tokens.");
            return new FeatBitJwtResources(key, null, null);
        }

        if (!options.IsPublishMode)
        {
            return new FeatBitJwtResources(null, null, null);
        }

        var privateKey = builder.AddParameter("jwt-private-key", secret: true)
            .WithDescription(
                $"PEM-encoded {options.Jwt.Algorithm} private key used to sign FeatBit API access tokens.");
        var publicKey = builder.AddParameter("jwt-public-key", secret: true)
            .WithDescription(
                $"PEM-encoded {options.Jwt.Algorithm} public key used to verify FeatBit API access tokens.");
        return new FeatBitJwtResources(null, privateKey, publicKey);
    }

    public static IResourceBuilder<ContainerResource> WithFeatBitJwt(
        this IResourceBuilder<ContainerResource> resource,
        FeatBitOptions options,
        FeatBitJwtResources jwt)
    {
        resource.WithEnvironment("Jwt__Algorithm", options.Jwt.Algorithm.ToString());

        if (options.Jwt.Algorithm == FeatBitJwtAlgorithm.HS256)
        {
            return resource.WithEnvironment(
                "Jwt__Key",
                jwt.SymmetricKey ?? throw new InvalidOperationException(
                    "The HS256 signing key parameter is missing."));
        }

        resource
            .WithEnvironment("Jwt__PrivateKeyPath", PrivateKeyContainerPath)
            .WithEnvironment("Jwt__PublicKeyPath", PublicKeyContainerPath);

        if (!options.IsPublishMode)
        {
            resource
                .WithBindMount(options.Jwt.PrivateKeyPath!, PrivateKeyContainerPath, isReadOnly: true)
                .WithBindMount(options.Jwt.PublicKeyPath!, PublicKeyContainerPath, isReadOnly: true);
        }

        return resource;
    }
}
