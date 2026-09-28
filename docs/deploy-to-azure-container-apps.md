# Deploy to Azure Container Apps

The AppHost deploys the FeatBit UI, API, and Evaluation services to Azure Container Apps. PostgreSQL, optional Redis, and an optional OpenTelemetry endpoint are external dependencies and must exist before deployment.

Local runs and Azure deployments use the same external PostgreSQL and Redis
configuration: PostgreSQL uses the `Parameters:postgres-*` values, and Standard mode
also requires `ConnectionStrings:redis`. See [Run locally](../README.md#run-locally)
for the development settings template.

## Prerequisites

- .NET 10 SDK
- Aspire CLI 13.4.6 or later
- Azure CLI
- An Azure subscription
- An external PostgreSQL database
- An external Redis service when deploying Standard mode

Before deploying, create a PostgreSQL database with a name of your choice and manually
initialize the FeatBit schema for the selected version using your database client.
Set `Parameters:postgres-database` to that database name in the settings below. The
default name is `featbit`; a different name is supported as long as it matches the
database you prepared. The PostgreSQL server must allow the `pg_trgm` extension used
by the FeatBit schema.

The AppHost connects to this existing database. It does not create the external
database or download, rewrite, or run schema initialization or migration scripts.
Later deployments reuse the database without running scripts automatically.

Ensure PostgreSQL and, when enabled, Redis are reachable from the Container Apps
environment. Use the complete Redis connection string required by your service,
including authentication and TLS settings where applicable.

Use a separate test resource group and database for this preview.

## Deploy

The production template uses HS256 JWT signing and disables OpenTelemetry. Complete the following steps in order. The current AppHost supports Standalone and Standard modes (PG version); it does not deploy the Professional Kafka and ClickHouse topology or the optional Control Plane. FeatBit v6 retires the standalone Data Analytics Server; analytics run in the API and Evaluation services.

### 1. Create the production settings file

Copy the non-secret template from the repository root.

PowerShell:

```powershell
Copy-Item appsettings.Production.example.json appsettings.Production.json
```

Bash:

```bash
cp appsettings.Production.example.json appsettings.Production.json
```

For the first deployment, replace only the `<...>` placeholders:

- Azure subscription, location, and resource group
- PostgreSQL host, port, user, and initialized database name

The template configures independent Azure Container Apps replica ranges under
`FeatBit:Azure:Ui`, `FeatBit:Azure:Api`, and `FeatBit:Azure:Els`. The defaults are UI
`1-3`, API `3-10`, and ELS `3-10`. Set a service's minimum and maximum to the same value
for a fixed replica count.

Keep the remaining defaults unless you already know that they must change. In particular, do not put the PostgreSQL password, JWT key, Redis connection string, OAuth client secrets, or OpenTelemetry authentication headers in this file. The local `appsettings.Production.json` file is ignored by Git.

### 2. Choose a deployment mode and set its environment variables

| Mode | `FeatBit:UseRedis` | Required environment variables |
| --- | --- | --- |
| Standalone: PostgreSQL only | `false` | PostgreSQL password and JWT key |
| Standard: PostgreSQL and Redis | `true` | PostgreSQL password, JWT key, and Redis connection string |

Environment variables apply to the current terminal session. Set them in the same terminal that will run `aspire deploy`.

#### Standalone mode

The production template already sets `FeatBit:UseRedis` to `false`.

PowerShell:

```powershell
$env:Parameters__postgres_password = "<postgres-password>"
$env:Parameters__jwt_key = "<stable-random-key-at-least-64-characters>"
```

Bash:

```bash
export Parameters__postgres_password="<postgres-password>"
export Parameters__jwt_key="<stable-random-key-at-least-64-characters>"
```

#### Standard mode

First set `FeatBit:UseRedis` to `true` in `appsettings.Production.json`, then set all three values below.

PowerShell:

```powershell
$env:Parameters__postgres_password = "<postgres-password>"
$env:Parameters__jwt_key = "<stable-random-key-at-least-64-characters>"
$env:ConnectionStrings__redis = "<redis-connection-string>"
```

Bash:

```bash
export Parameters__postgres_password="<postgres-password>"
export Parameters__jwt_key="<stable-random-key-at-least-64-characters>"
export ConnectionStrings__redis="<redis-connection-string>"
```

Use the same stable JWT key for every deployment. Changing it invalidates existing access tokens. See the [FeatBit v6.0.0-preview JWT configuration](https://github.com/featbit/featbit/tree/6.0.0-preview/modules/back-end#jwt) if you need RS256 or ES256 instead of the default HS256.

Aspire parameter names use hyphens, but their environment variable form uses underscores. For example, `postgres-database` becomes `Parameters__postgres_database`, `postgres-password` becomes `Parameters__postgres_password`, and `jwt-key` becomes `Parameters__jwt_key`.

### 3. Sign in and deploy

Run these commands in the same terminal where the mode-specific environment variables were set:

```shell
az login
az account show --query "{name:name,id:id,tenantId:tenantId}" --output table

# Optional: preview the deployment pipeline without creating resources.
aspire deploy --apphost ./apphost.csproj --environment Production --list-steps

# Deploy to Azure Container Apps.
aspire deploy --apphost ./apphost.csproj --environment Production
```

`--environment Production` loads `appsettings.Production.json`. Environment variables override values from the file.

Run the first deployment in an interactive terminal so Azure tenant selection or missing parameter prompts can be handled. Do not pipe the command through another process because that can disable interactive prompts.

### CI/CD deployment

Because `appsettings.Production.json` is not committed, CI/CD should provide its values as environment variables as well.

Both modes require:

- `Azure__SubscriptionId`, `Azure__Location`, `Azure__ResourceGroup`
- `FeatBit__Azure__Ui__MinReplicas`, `FeatBit__Azure__Ui__MaxReplicas`
- `FeatBit__Azure__Api__MinReplicas`, `FeatBit__Azure__Api__MaxReplicas`
- `FeatBit__Azure__Els__MinReplicas`, `FeatBit__Azure__Els__MaxReplicas`
- `Parameters__postgres_host`, `Parameters__postgres_port`, `Parameters__postgres_user`, `Parameters__postgres_database`
- `Parameters__postgres_password`, `Parameters__jwt_key`
- `FeatBit__OpenTelemetry__Enabled=false`, unless an OTLP endpoint is configured

Then choose one mode:

- Standalone: `FeatBit__UseRedis=false`
- Standard: `FeatBit__UseRedis=true` and `ConnectionStrings__redis=<redis-connection-string>`

After the Azure login context and every required value are configured, deploy without prompts:

```shell
aspire deploy --apphost ./apphost.csproj --environment Production --non-interactive
```

For other configuration paths, replace `:` with `__`. For example, `FeatBit:Api:Environment:SSOEnabled` becomes `FeatBit__Api__Environment__SSOEnabled`.

That completes the deployment flow. The remaining sections are optional configuration references.

## Optional FeatBit service configuration

You can skip this section for the first deployment. The template and FeatBit container images already provide working defaults.

Service-specific non-secret values go in these sections of `appsettings.Production.json`:

- `FeatBit:Ui:Environment`
- `FeatBit:Api:Environment`
- `FeatBit:Els:Environment`

The list below was checked against the FeatBit 6.0.0-preview source. Native environment variable names such as `Cors__AllowedOrigins` can be added to the corresponding `Environment` section without changing the AppHost code.

### UI

| Environment variable | Purpose |
| --- | --- |
| `DEMO_URL` | Dino demo URL. |
| `BASE_HREF` | Path base when hosting the UI below a path such as `/featbit/`. |
| `DISPLAY_API_URL` | Optional API URL shown in Getting Started. |
| `DISPLAY_EVALUATION_URL` | Optional Event/Streaming URL shown in Getting Started. |
| `HOSTING_MODE` | UI hosting mode; defaults to `self-hosted`. |

`API_URL` and `EVALUATION_URL` are generated from Aspire endpoints and must not be overridden. See the [FeatBit 6.0.0-preview UI environment reference](https://github.com/featbit/featbit/tree/6.0.0-preview/modules/front-end#environment-variables).

### API authentication and SSO

Set `SSOEnabled` to `true` under `FeatBit:Api:Environment` to enable workspace OIDC SSO endpoints. The OIDC provider details remain workspace data configured through FeatBit after deployment.

Google and GitHub social login are separate from workspace OIDC SSO. Configure the public client ID normally, but map the client secret to an Aspire secret parameter:

```json
{
  "FeatBit": {
    "Api": {
      "Environment": {
        "OAuthProviders__0__Name": "Google",
        "OAuthProviders__0__ClientId": "<google-client-id>"
      },
      "SecretParameters": {
        "OAuthProviders__0__ClientSecret": "api-google-client-secret"
      }
    }
  }
}
```

Provide the secret in the terminal before deploying:

PowerShell:

```powershell
$env:Parameters__api_google_client_secret = "<google-client-secret>"
```

Bash:

```bash
export Parameters__api_google_client_secret="<google-client-secret>"
```

Use index `1` for a second provider and set its name to `GitHub`. FeatBit 6.0.0-preview supports the case-sensitive names `Google` and `GitHub`.

Other API settings include `Jwt__Issuer`, `Jwt__Audience`, `UsageTracking__FlushIntervalMs`, `UsageTracking__ChannelCapacity`, Redis population timeouts, `AllowedHosts`, and `Logging__...`. 

### Evaluation server (ELS)

| Group | Environment variables |
| --- | --- |
| Streaming | `Streaming__TrackClientHostName`, `Streaming__TokenExpirySeconds` |
| CORS | `Cors__Enabled`, `Cors__AllowedOrigins`, `Cors__AllowedHeaders`, `Cors__AllowedMethods`, `Cors__AllowCredentials` |
| Rate limiting | `RateLimiting__Enabled`, `RateLimiting__Distributed`, `RateLimiting__Type`, `RateLimiting__PermitLimit`, `RateLimiting__WindowSeconds`, `RateLimiting__QueueLimit`, `RateLimiting__SegmentsPerWindow`, `RateLimiting__TokenLimit`, `RateLimiting__TokensPerPeriod`, `RateLimiting__ReplenishmentPeriodSeconds` |

Use semicolons to separate explicit CORS values. `Cors__AllowCredentials=true` cannot be combined with `Cors__AllowedOrigins=*`.

`RateLimiting__Type` accepts `FixedWindow`, `SlidingWindow`, or `TokenBucket`. Distributed rate limiting requires both `FeatBit:UseRedis=true` and `RateLimiting__Distributed=true`; otherwise every ELS replica applies its own limits. Per-endpoint overrides use `RateLimiting__Endpoints__<Key>__<Property>`.

See the [FeatBit 6.0.0-preview ELS environment reference](https://github.com/featbit/featbit/tree/6.0.0-preview/modules/evaluation-server#environment-variables).

### Values managed by Aspire

The AppHost manages `VERSION`, UI endpoint URLs, backend database/queue/cache connections, OpenTelemetry variables, and API JWT signing material. Configure those through the top-level `FeatBit`, `Parameters`, and `ConnectionStrings` sections instead of service `Environment` entries.

`SecretParameters` contains only a mapping to an Aspire parameter name, never the secret value. Parameter names may contain only lowercase letters, digits, and hyphens and must not exceed 63 characters. UI configuration is browser-visible, so `FeatBit:Ui:SecretParameters` is rejected.

## Optional OpenTelemetry export

To export telemetry from Azure Container Apps, update these sections in `appsettings.Production.json`:

```json
{
  "FeatBit": {
    "OpenTelemetry": {
      "Enabled": true,
      "UseHeaders": false,
      "Insecure": false
    }
  },
  "Parameters": {
    "otel-exporter-otlp-endpoint": "https://<otel-collector-host>:4317"
  }
}
```

Keep the existing PostgreSQL parameter entries when editing the `Parameters` section. The endpoint is non-secret and can be stored in the local production settings file.

In CI/CD, provide it as `Parameters__otel_exporter_otlp_endpoint` instead.

If the collector requires authentication headers, set `FeatBit:OpenTelemetry:UseHeaders` to `true` in `appsettings.Production.json` and provide the header value as a secret:

PowerShell:

```powershell
$env:Parameters__otel_exporter_otlp_headers = "<header-name>=<header-value>"
```

Bash:

```bash
export Parameters__otel_exporter_otlp_headers="<header-name>=<header-value>"
```
