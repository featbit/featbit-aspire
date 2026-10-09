# Deploy to Azure Container Apps

The AppHost deploys the FeatBit UI, API, and Evaluation services to Azure Container Apps using external PostgreSQL and optional Redis. Telemetry can use the ACA Aspire dashboard or an external collector.

Local runs use Docker PostgreSQL and Redis by default. Azure publish/deploy always
uses external dependencies: PostgreSQL uses the `Parameters:postgres-*` values,
and Standard mode also requires `ConnectionStrings:redis`. To use these external
services locally, set `FeatBit:UseLocalInfrastructure=false`. See
[Run locally](../README.md#run-locally) for the development settings template.

## Prerequisites

- .NET 10 SDK
- Aspire CLI 13.6.0 or later
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

For an existing database, apply only the migrations newer than its current
schema. A database with the v5.4.1 schema needs
[`v6.0.0.sql`](../infra/postgresql/6.0.0/docker-entrypoint-initdb.d/v6.0.0.sql)
before running FeatBit v6. The bundled scripts start with `\connect featbit`;
when using a different database name, change that line in a working copy or
remove it when running SQL through a client already connected to the target.
The v6 script preserves the old `events`, `experiments`, and `experiment_metrics`
tables with `_legacy` names and creates the new experiment schema. It does not
copy those legacy experiments into the v6 tables.

Ensure PostgreSQL and, when enabled, Redis are reachable from the Container Apps
environment. Use the complete Redis connection string required by your service,
including authentication and TLS settings where applicable.

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

Use the same stable JWT key for every deployment. Changing it invalidates existing access tokens. See the [FeatBit v6.0.0 JWT configuration](https://github.com/featbit/featbit/tree/6.0.0/modules/back-end#jwt) if you need RS256 or ES256 instead of the default HS256.

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

PostgreSQL parameters use the current settings file and environment variables
before cached deployment inputs. After editing `appsettings.Production.json`,
run the same `aspire deploy` command in the terminal with the required secret
environment variables set. A cached PostgreSQL password is reused only when no
current value is supplied. Other parameters retain Aspire's normal caching
behavior.

To bypass all deployment caching, add `--clear-cache`. This clears the local
deployment cache and does not save state for that deployment. Set
`Azure:SubscriptionId`, `Azure:ResourceGroup`, and `Azure:Location` explicitly in
the production settings or environment variables so the same Azure resources
are updated. Existing domain and certificate bindings are read from Azure and
preserved even when no deployment state is saved.

Each applied Container App template generates a new revision so database,
Redis, JWT, and OAuth secret changes are loaded by new containers. Unchanged
applications can still be skipped by the deployment cache. Existing domains and
certificate bindings are preserved during the update. ACA secret updates made
directly in Azure, outside this AppHost deployment, still require a new revision
or a restart of the active revision.

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
- `FeatBit__OpenTelemetry__Enabled=false`, or an [enabled export target](#optional-opentelemetry-export)

Then choose one mode:

- Standalone: `FeatBit__UseRedis=false`
- Standard: `FeatBit__UseRedis=true` and `ConnectionStrings__redis=<redis-connection-string>`

After the Azure login context and every required value are configured, deploy without prompts:

```shell
aspire deploy --apphost ./apphost.csproj --environment Production --non-interactive
```

For other configuration paths, replace `:` with `__`. For example, `FeatBit:Authentication:SsoEnabled` becomes `FeatBit__Authentication__SsoEnabled`.

That completes the deployment flow. The remaining sections are optional configuration references.

## Optional FeatBit service configuration

You can skip this section for the first deployment. The template and FeatBit container images already provide working defaults.

Service-specific non-secret values go in these sections of `appsettings.Production.json`:

- `FeatBit:Ui:Environment`
- `FeatBit:Api:Environment`
- `FeatBit:Els:Environment`

The list below was checked against the FeatBit 6.0.0 source. Native environment variable names such as `Cors__AllowedOrigins` can be added to the corresponding `Environment` section without changing the AppHost code.

### UI

| Environment variable | Purpose |
| --- | --- |
| `DEMO_URL` | Dino demo URL. |
| `BASE_HREF` | Path base when hosting the UI below a path such as `/featbit/`. |
| `DISPLAY_API_URL` | Optional API URL shown in Getting Started. |
| `DISPLAY_EVALUATION_URL` | Optional Event/Streaming URL shown in Getting Started. |
| `HOSTING_MODE` | UI hosting mode; defaults to `self-hosted`. |

The AppHost sets `API_URL` and `EVALUATION_URL` from Aspire endpoints by default.
To make the browser use custom API and ELS domains, merge these fields into
`appsettings.Production.json`, preserving the other UI settings:

```json
{
  "FeatBit": {
    "Ui": {
      "ApiUrl": "https://api.featbit.io",
      "EvaluationUrl": "https://eval.featbit.io"
    }
  }
}
```

These domains are examples; replace them with the domains you control and bind to
the corresponding services. Alternatively, configure the same values in the deployment terminal:

```powershell
$env:FeatBit__Ui__ApiUrl = "https://api.featbit.io"
$env:FeatBit__Ui__EvaluationUrl = "https://eval.featbit.io"
```

Each URL is optional and independent. An omitted, empty, or whitespace value keeps
Aspire's generated URL for that service. Explicit values apply in both local and
publish modes; putting them only in `appsettings.Production.json` leaves ordinary
local development using its generated localhost URLs. Use absolute HTTP(S) URLs
without credentials, query strings, or fragments. Trailing slashes are removed.
`EvaluationUrl` must use HTTP(S), not WS(S); the UI derives its streaming URL from it.
Do not put `API_URL` or `EVALUATION_URL` under `Ui:Environment`.

These settings configure the UI's requests only. They do not create DNS records,
certificates, or ACA domain bindings. For the first deployment, you can set the final
URLs in advance, deploy to obtain the generated ACA domain names, then configure DNS
and HTTPS bindings for your custom domains on the corresponding Container Apps.
Browser requests to these URLs will fail until DNS and HTTPS are ready. Once they are
ready, refresh the UI; another UI deployment is not needed.

Subsequent `aspire deploy` runs automatically read each Container App's current
custom domain bindings from Azure and include them in the update. Existing
hostnames, certificates, and binding states are preserved without a local domain
list. Manage domain additions, removals, and certificate changes in Azure as usual.

Each Container App update depends on its domain lookup succeeding. Authentication,
network, and unexpected Azure errors stop that update instead of treating the
bindings as empty. A confirmed missing app starts with no bindings on its first
deployment. Local development does not query Azure.

`aspire publish` remains offline: the generated templates require an
`existingCustomDomains` array, with no empty default. If applying those templates
outside `aspire deploy`, supply the current Azure bindings for every app.
See [ACA custom domains and managed certificates](https://learn.microsoft.com/en-us/azure/container-apps/custom-domains-managed-certificates).

`DISPLAY_API_URL` and `DISPLAY_EVALUATION_URL` only change the addresses shown in
Getting Started; they do not replace these browser request URLs. See the
[FeatBit 6.0.0 UI environment reference](https://github.com/featbit/featbit/tree/6.0.0/modules/front-end#environment-variables).

### API authentication and SSO

ACA deployments default the UI's `HOSTING_MODE` to `saas`, so the initial page is
**Sign in to your workspace** with email and password fields. Enabling SSO keeps
the **Sign in with SSO** button on that page. This UI setting does not enable or
disable API authentication providers.

Override the UI mode through `FeatBit:Ui:Environment:HOSTING_MODE` if needed.
The upstream `self-hosted` mode opens the SSO page first when SSO is enabled;
local runs retain that upstream default.

Both development and production templates include `FeatBit:Authentication` with
optional GitHub, Google, and workspace OIDC SSO settings. Configure these in
`appsettings.Development.json` for local runs or `appsettings.Production.json` for
deployment.

For each social provider, set `Enabled=true` and its public `ClientId` under
`FeatBit:Authentication:GitHub` or `FeatBit:Authentication:Google`. Before deployment,
supply `Parameters__api_github_client_secret` and/or
`Parameters__api_google_client_secret` in the deployment terminal or CI secret
store. The AppHost maps these to API environment variables using ACA secrets;
disabled providers require no secret parameters.

Register OAuth callbacks on the UI URL, for example
`http://localhost:8081/en/login?social-logged-in=true` locally or
`https://<ui-host>/en/login?social-logged-in=true` in production. Register `/zh/login`
as well if using Chinese, and include any UI base path. OIDC SSO uses the same
login paths with `?sso-logged-in=true` instead. The full callback must match the
provider's registered redirect URI.

Set `FeatBit:Authentication:SsoEnabled=true` to enable the API's workspace OIDC SSO
endpoints. The workspace must also have a license granting SSO and OIDC settings
configured through FeatBit. The AppHost does not provision an identity provider or
replace workspace settings. Existing native API authentication configuration is
still supported. When migrating, remove `SSOEnabled` from `Api:Environment` or
`Api:SecretParameters` before setting `Authentication:SsoEnabled`, and remove
`OAuthProviders__*` entries from both sections before enabling a named provider.
Mixed configuration is rejected to prevent conflicting settings.

Other API settings include `Jwt__Issuer`, `Jwt__Audience`, `UsageTracking__FlushIntervalMs`, `UsageTracking__ChannelCapacity`, Redis population timeouts, `AllowedHosts`, and `Logging__...`. 

### Evaluation server (ELS)

| Group | Environment variables |
| --- | --- |
| Streaming | `Streaming__TrackClientHostName`, `Streaming__TokenExpirySeconds` |
| CORS | `Cors__Enabled`, `Cors__AllowedOrigins`, `Cors__AllowedHeaders`, `Cors__AllowedMethods`, `Cors__AllowCredentials` |
| Rate limiting | `RateLimiting__Enabled`, `RateLimiting__Distributed`, `RateLimiting__Type`, `RateLimiting__PermitLimit`, `RateLimiting__WindowSeconds`, `RateLimiting__QueueLimit`, `RateLimiting__SegmentsPerWindow`, `RateLimiting__TokenLimit`, `RateLimiting__TokensPerPeriod`, `RateLimiting__ReplenishmentPeriodSeconds` |

Use semicolons to separate explicit CORS values. `Cors__AllowCredentials=true` cannot be combined with `Cors__AllowedOrigins=*`.

`RateLimiting__Type` accepts `FixedWindow`, `SlidingWindow`, or `TokenBucket`. Distributed rate limiting requires both `FeatBit:UseRedis=true` and `RateLimiting__Distributed=true`; otherwise every ELS replica applies its own limits. Per-endpoint overrides use `RateLimiting__Endpoints__<Key>__<Property>`.

See the [FeatBit 6.0.0 ELS environment reference](https://github.com/featbit/featbit/tree/6.0.0/modules/evaluation-server#environment-variables).

### Values managed by Aspire

The AppHost manages `VERSION`, UI endpoint URLs, backend database/queue/cache connections, OpenTelemetry variables, and API JWT signing material. Configure those through the top-level `FeatBit`, `Parameters`, and `ConnectionStrings` sections instead of service `Environment` entries. For UI request URL overrides, use `FeatBit:Ui:ApiUrl` and `FeatBit:Ui:EvaluationUrl`.

`SecretParameters` contains only a mapping to an Aspire parameter name, never the secret value. Parameter names may contain only lowercase letters, digits, and hyphens and must not exceed 63 characters. UI configuration is browser-visible, so `FeatBit:Ui:SecretParameters` is rejected.

## Optional OpenTelemetry export

### ACA Aspire dashboard

To enable API and Evaluation structured logs, traces, and metrics in the ACA
Aspire dashboard, merge this into `appsettings.Production.json`:

```json
{
  "FeatBit": {
    "OpenTelemetry": {
      "Enabled": true,
      "ExportTarget": "AzureDashboard"
    }
  }
}
```

ACA supplies the OTLP endpoint; no collector parameters are needed. Set
`Enabled=false` to disable application telemetry while keeping console logs.
Redeploy to apply changes.

`ExportTarget` applies only in publish mode. Local runs use the local Aspire
dashboard. UI browser telemetry requires separate frontend instrumentation.

### External collector

For an external OTLP/gRPC collector, use:

```json
{
  "FeatBit": {
    "OpenTelemetry": {
      "Enabled": true,
      "ExportTarget": "ExternalCollector",
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

Omitting `ExportTarget` retains the previous `ExternalCollector` behavior.

In CI/CD, provide it as `Parameters__otel_exporter_otlp_endpoint` instead.

Set `Insecure=true` for an external collector using plaintext gRPC; leave it
`false` for TLS.

If the collector requires authentication headers, set `FeatBit:OpenTelemetry:UseHeaders` to `true` in `appsettings.Production.json` and provide the header value as a secret:

PowerShell:

```powershell
$env:Parameters__otel_exporter_otlp_headers = "<header-name>=<header-value>"
```

Bash:

```bash
export Parameters__otel_exporter_otlp_headers="<header-name>=<header-value>"
```
