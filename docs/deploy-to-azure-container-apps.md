# Deploy to Azure Container Apps

The AppHost deploys the FeatBit UI, API, and Evaluation services to Azure Container Apps. PostgreSQL, optional Redis, and an optional OpenTelemetry endpoint are external dependencies and must exist before deployment.

## Prerequisites

- .NET 10 SDK
- Aspire CLI 13.4.6 or later
- Azure CLI
- An Azure subscription
- An external PostgreSQL server
- An external Redis service when deploying Standard mode

Create the `featbit` PostgreSQL database, then download and run the SQL files from the
upstream FeatBit tag that matches `Version` in `apphost.csproj`. Run the files in
version order before deploying. For the current target, use the
[FeatBit 5.4.4 PostgreSQL initialization files](https://github.com/featbit/featbit/tree/5.4.4/infra/postgresql/docker-entrypoint-initdb.d).
The URL pattern for other releases is:

```text
https://github.com/featbit/featbit/tree/{version}/infra/postgresql/docker-entrypoint-initdb.d
```

## Configure the deployment

Set non-secret values directly in the deployment environment and obtain secret values from your local secret store or CI/CD secret store. Do not commit production credentials to `appsettings*.json`, `aspire.config.json`, or pipeline YAML.

The following examples disable OpenTelemetry to keep the initial deployment minimal.

PowerShell:

```powershell
$env:Azure__SubscriptionId = "<subscription-id>"
$env:Azure__Location = "<azure-region>"
$env:Azure__ResourceGroup = "<resource-group>"
$env:FeatBit__Azure__MinReplicas = "1"
$env:FeatBit__Azure__MaxReplicas = "10"

$env:Parameters__postgres_host = "<postgres-host>"
$env:Parameters__postgres_port = "5432"
$env:Parameters__postgres_user = "<postgres-user>"
$env:Parameters__postgres_password = "<postgres-password>"

$env:FeatBit__Jwt__Algorithm = "HS256"
$env:Parameters__jwt_key = "<stable-random-key-at-least-64-characters>"
$env:FeatBit__OpenTelemetry__Enabled = "false"
```

Bash:

```bash
export Azure__SubscriptionId="<subscription-id>"
export Azure__Location="<azure-region>"
export Azure__ResourceGroup="<resource-group>"
export FeatBit__Azure__MinReplicas=1
export FeatBit__Azure__MaxReplicas=10

export Parameters__postgres_host="<postgres-host>"
export Parameters__postgres_port="5432"
export Parameters__postgres_user="<postgres-user>"
export Parameters__postgres_password="<postgres-password>"

export FeatBit__Jwt__Algorithm=HS256
export Parameters__jwt_key="<stable-random-key-at-least-64-characters>"
export FeatBit__OpenTelemetry__Enabled=false
```

The replica range applies to the UI, API, and Evaluation Container Apps. It defaults to 1–10. Set the minimum and maximum to the same value for a fixed replica count; keep the minimum at 1 or higher to avoid scale-to-zero cold starts.

See [FeatBit v5.4.4 JWT configuration](https://github.com/featbit/featbit/tree/5.4.4/modules/back-end#jwt) for other signing options.

### Standalone: PostgreSQL only

PowerShell:

```powershell
$env:FeatBit__UseRedis = "false"
```

Bash:

```bash
export FeatBit__UseRedis=false
```

### Standard: PostgreSQL and Redis

PowerShell:

```powershell
$env:FeatBit__UseRedis = "true"
$env:ConnectionStrings__redis = "<redis-connection-string>"
```

Bash:

```bash
export FeatBit__UseRedis=true
export ConnectionStrings__redis="<redis-connection-string>"
```

## Deploy

Sign in, preview the Aspire deployment pipeline, and deploy:

PowerShell:

```powershell
az login
aspire deploy --apphost ./apphost.csproj --environment Production --list-steps
aspire deploy --apphost ./apphost.csproj --environment Production
```

Bash:

```bash
az login
aspire deploy --apphost ./apphost.csproj --environment Production --list-steps
aspire deploy --apphost ./apphost.csproj --environment Production
```

The first deployment should run in an interactive terminal so Azure tenant selection and unresolved parameters can be handled. In CI/CD, authenticate to Azure before the deploy step, provide the same configuration keys as environment variables, and disable prompts:

PowerShell:

```powershell
aspire deploy --apphost ./apphost.csproj --environment Production --non-interactive
```

Bash:

```bash
aspire deploy --apphost ./apphost.csproj --environment Production --non-interactive
```

Parameter names containing a dash use an underscore in environment variables. For example, `jwt-key` becomes `Parameters__jwt_key`, and `postgres-password` becomes `Parameters__postgres_password`.

## Optional OpenTelemetry export

OpenTelemetry is enabled by default. To export telemetry from Azure Container Apps, configure an external OTLP/gRPC endpoint instead of disabling it:

PowerShell:

```powershell
$env:FeatBit__OpenTelemetry__Enabled = "true"
$env:FeatBit__OpenTelemetry__Insecure = "false"
$env:Parameters__otel_exporter_otlp_endpoint = "https://<otel-collector-host>:4317"
```

Bash:

```bash
export FeatBit__OpenTelemetry__Enabled=true
export FeatBit__OpenTelemetry__Insecure=false
export Parameters__otel_exporter_otlp_endpoint="https://<otel-collector-host>:4317"
```

If the collector requires headers, provide them as a secret:

PowerShell:

```powershell
$env:FeatBit__OpenTelemetry__UseHeaders = "true"
$env:Parameters__otel_exporter_otlp_headers = "<header-name>=<header-value>"
```

Bash:

```bash
export FeatBit__OpenTelemetry__UseHeaders=true
export Parameters__otel_exporter_otlp_headers="<header-name>=<header-value>"
```
