# FeatBit Aspire

FeatBit Aspire uses .NET Aspire to deploy FeatBit to Azure Container Apps and run it locally.

This branch targets [FeatBit v6.0.0](https://github.com/featbit/featbit/releases/tag/6.0.0) and supports only the Standalone (PostgreSQL) and Standard (PostgreSQL + Redis) deployment modes.

The version in `apphost.csproj` selects all three container images:

- `featbit/featbit-api-server:6.0.0`
- `featbit/featbit-ui:6.0.0`
- `featbit/featbit-evaluation-server:6.0.0`

## Deploy to Azure Container Apps

> [!IMPORTANT]
> Follow the complete [Deploy to Azure Container Apps](docs/deploy-to-azure-container-apps.md)
> guide from start to finish.

## Run locally

Install the .NET 10 SDK, Aspire CLI 13.6.0 or later, and Docker or another compatible container runtime.

Start Docker, then run the AppHost from the repository root:

```sh
aspire run
```

Local runs start the FeatBit UI, API, Evaluation, PostgreSQL, and Redis containers.
Aspire generates local credentials and assigns PostgreSQL/Redis host ports. The
`featbit` database is initialized automatically from the bundled v6.0.0 SQL scripts
on its first start. The API and Evaluation services wait for the database and Redis
to be ready.

PostgreSQL and Redis use persistent Docker volumes and remain running across
AppHost restarts. Initialization runs only on an empty PostgreSQL volume; existing
local data is retained. In the dashboard, these resources appear as `local-postgres`,
`featbit-db`, and `local-redis`.

Open `http://localhost:8081` once the resources are ready.

No development settings file or external database credentials are needed for the
default local setup. To customize local settings, copy the non-secret template
if you do not already have `appsettings.Development.json`:

PowerShell:

```powershell
Copy-Item appsettings.Development.example.json appsettings.Development.json
```

Bash:

```bash
cp appsettings.Development.example.json appsettings.Development.json
```

`FeatBit:UseLocalInfrastructure` defaults to `true` for local runs. External
PostgreSQL parameters and Redis connection strings are ignored in this mode.
`FeatBit:UseRedis` defaults to `true` with local infrastructure; set it to `false`
to run Standalone with Docker PostgreSQL only.

### Use external services locally

Set `FeatBit:UseLocalInfrastructure=false` to connect to an external PostgreSQL
database and, in Standard mode, external Redis. Azure publish/deploy always uses
external dependencies, regardless of this local switch.

In `appsettings.Development.json`, add `Parameters:postgres-host`,
`postgres-port`, `postgres-user`, and `postgres-database` for your external database.
Environment variables can also supply these settings, for example
`Parameters__postgres_host` and `Parameters__postgres_database`.

Create the external database and manually initialize the FeatBit schema for the
selected version using your database client before starting the application. The
AppHost reuses this external database and does not download, rewrite, or run schema
initialization or migration scripts. PostgreSQL must allow the `pg_trgm` extension
used by the schema.

Keep passwords and Redis connection strings in environment variables rather than
the settings file. `appsettings.Development.json` is ignored by Git; do not commit
credentials. Set the following values in the same terminal that will run Aspire.

Run Standalone with external PostgreSQL only:

PowerShell:

```powershell
$env:FeatBit__UseRedis = "false"
$env:FeatBit__UseLocalInfrastructure = "false"
$env:Parameters__postgres_password = "<postgres-password>"
aspire run
```

Bash:

```bash
export FeatBit__UseRedis=false
export FeatBit__UseLocalInfrastructure=false
export Parameters__postgres_password="<postgres-password>"
aspire run
```

Run Standard with external PostgreSQL and Redis:

PowerShell:

```powershell
$env:FeatBit__UseRedis = "true"
$env:FeatBit__UseLocalInfrastructure = "false"
$env:Parameters__postgres_password = "<postgres-password>"
$env:ConnectionStrings__redis = "<redis-connection-string>"
aspire run
```

Bash:

```bash
export FeatBit__UseRedis=true
export FeatBit__UseLocalInfrastructure=false
export Parameters__postgres_password="<postgres-password>"
export ConnectionStrings__redis="<redis-connection-string>"
aspire run
```

Use the complete Redis connection string required by your service, including
authentication and TLS settings where applicable. PostgreSQL and Redis use the same
configuration keys for local runs and [Azure deployments](docs/deploy-to-azure-container-apps.md).

In this mode, external connections appear in Resources as `postgres-connection`
and, in Standard mode, `redis-connection`. The Redis connection string remains a
secret named `redis` under Parameters.

Open `http://localhost:8081` once the resources are ready. If the backend services
started before the database schema was initialized, restart the API and Evaluation
services from the Aspire dashboard after completing initialization.

## GitHub, Google, and SSO login

Configure `FeatBit:Authentication` in the development or production settings file
to enable GitHub OAuth, Google OAuth, or workspace OIDC SSO. The AppHost uses the
same settings locally and in Azure, and passes OAuth client secrets through Aspire
secret parameters. The templates leave these providers disabled until configured.

See [API authentication and SSO](docs/deploy-to-azure-container-apps.md#api-authentication-and-sso)
for configuration and callback URLs. SSO also requires a workspace license granting
the feature.
