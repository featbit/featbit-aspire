using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;

namespace FeatBit.AppHost;

public static class FeatBitJwtAzureExtensions
{
    public static void ConfigureAzureContainerApp(
        this FeatBitJwtResources jwt,
        AzureResourceInfrastructure infrastructure,
        ContainerApp app,
        FeatBitOptions options)
    {
        if (options.Jwt.Algorithm == FeatBitJwtAlgorithm.HS256)
        {
            return;
        }

        const string privateKeySecretName = "jwt-private-key";
        const string publicKeySecretName = "jwt-public-key";
        const string keyVolumeName = "jwt-keys";

        app.Configuration.Secrets.Add(new ContainerAppWritableSecret
        {
            Name = privateKeySecretName,
            Value = jwt.PrivateKey!.AsProvisioningParameter(infrastructure, "jwtPrivateKey")
        });
        app.Configuration.Secrets.Add(new ContainerAppWritableSecret
        {
            Name = publicKeySecretName,
            Value = jwt.PublicKey!.AsProvisioningParameter(infrastructure, "jwtPublicKey")
        });

        var keyVolume = new ContainerAppVolume
        {
            Name = keyVolumeName,
            StorageType = ContainerAppStorageType.Secret
        };
        keyVolume.Secrets.Add(new SecretVolumeItem
        {
            SecretRef = privateKeySecretName,
            Path = "private.pem"
        });
        keyVolume.Secrets.Add(new SecretVolumeItem
        {
            SecretRef = publicKeySecretName,
            Path = "public.pem"
        });
        app.Template.Volumes.Add(keyVolume);
        app.Template.Containers[0].Unwrap().VolumeMounts.Add(new ContainerAppVolumeMount
        {
            VolumeName = keyVolumeName,
            MountPath = "/app/secrets/jwt"
        });
    }
}
