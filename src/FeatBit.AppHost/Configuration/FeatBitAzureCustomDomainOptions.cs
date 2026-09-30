using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace FeatBit.AppHost;

public sealed record FeatBitAzureCustomDomainOptions(string Name, string CertificateId)
{
    public static IReadOnlyList<FeatBitAzureCustomDomainOptions> Load(IConfigurationSection section)
    {
        var domains = new List<FeatBitAzureCustomDomainOptions>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in section.GetChildren())
        {
            var name = entry["Name"]?.Trim();
            var certificateId = entry["CertificateId"]?.Trim();
            var dnsName = name?.StartsWith("*.", StringComparison.Ordinal) == true ? name[2..] : name;
            if (string.IsNullOrEmpty(dnsName) || Uri.CheckHostName(dnsName) != UriHostNameType.Dns)
            {
                throw new InvalidOperationException(
                    $"{entry.Path}:Name must be a DNS hostname without a scheme, port, or path.");
            }

            if (string.IsNullOrEmpty(certificateId) || !Regex.IsMatch(
                    certificateId,
                    @"^/subscriptions/[0-9a-f-]{36}/resourceGroups/[^/]+/providers/Microsoft\.App/managedEnvironments/[^/]+/(managedCertificates|certificates)/[^/]+$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException(
                    $"{entry.Path}:CertificateId must be the full resource ID of an existing " +
                    "Container Apps environment certificate or managed certificate.");
            }

            if (!names.Add(name!))
            {
                throw new InvalidOperationException($"{section.Path} contains duplicate hostname '{name}'.");
            }

            domains.Add(new FeatBitAzureCustomDomainOptions(name!, certificateId));
        }

        return domains;
    }
}
