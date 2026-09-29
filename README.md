# FeatBit Aspire

FeatBit Aspire uses .NET Aspire to deploy FeatBit to Azure Container Apps and run it locally.

This branch targets [FeatBit v6.0.0-preview](https://github.com/featbit/featbit/releases/tag/6.0.0-preview) and supports only the Standalone (PostgreSQL) and Standard (PostgreSQL + Redis) deployment modes.

> [!IMPORTANT]
> This is a preview release for evaluation and feedback. Use a separate test environment
> and database.

The version in `apphost.csproj` selects all three container images:

- `featbit/featbit-api-server:6.0.0-preview`
- `featbit/featbit-ui:6.0.0-preview`
- `featbit/featbit-evaluation-server:6.0.0-preview`

## Deploy to Azure Container Apps

> [!IMPORTANT]
> Follow the complete [Deploy to Azure Container Apps](docs/deploy-to-azure-container-apps.md)
> guide from start to finish.

## Run locally

Install the .NET 10 SDK, Aspire CLI 13.5.4 or later, and Docker or another compatible container runtime.

Local runs start the FeatBit UI, API, and Evaluation containers and connect to an
external PostgreSQL database. Standard mode also connects to external Redis. These
services must be reachable from the local containers.

Copy the non-secret development settings template:

PowerShell:

```powershell
Copy-Item appsettings.Development.example.json appsettings.Development.json
```

Bash:

```bash
cp appsettings.Development.example.json appsettings.Development.json
```

In `appsettings.Development.json`, fill in `Parameters:postgres-host`,
`postgres-port`, and `postgres-user`. Set `Parameters:postgres-database` to your
database name; the default is `featbit`, and any name matching your prepared
database is supported. Environment variables override these settings, for example
`Parameters__postgres_database` overrides the database name.

Create the external database and manually initialize the FeatBit schema for the
selected version using your database client before starting the application. The
AppHost reuses this database and does not download, rewrite, or run schema
initialization or migration scripts. PostgreSQL must allow the `pg_trgm` extension
used by the schema.

Keep passwords and Redis connection strings in environment variables rather than
the settings file. `appsettings.Development.json` is ignored by Git; do not commit
credentials. Set the following values in the same terminal that will run Aspire.

Run Standalone with external PostgreSQL only:

PowerShell:

```powershell
$env:FeatBit__UseRedis = "false"
$env:Parameters__postgres_password = "<postgres-password>"
aspire run
```

Bash:

```bash
export FeatBit__UseRedis=false
export Parameters__postgres_password="<postgres-password>"
aspire run
```

Run Standard with external PostgreSQL and Redis:

PowerShell:

```powershell
$env:FeatBit__UseRedis = "true"
$env:Parameters__postgres_password = "<postgres-password>"
$env:ConnectionStrings__redis = "<redis-connection-string>"
aspire run
```

Bash:

```bash
export FeatBit__UseRedis=true
export Parameters__postgres_password="<postgres-password>"
export ConnectionStrings__redis="<redis-connection-string>"
aspire run
```

Use the complete Redis connection string required by your service, including
authentication and TLS settings where applicable. PostgreSQL and Redis use the same
configuration keys for local runs and [Azure deployments](docs/deploy-to-azure-container-apps.md).

In the Aspire dashboard, external connections appear in Resources as `postgres-connection`
and, in Standard mode, `redis-connection`. The Redis connection string remains a
secret named `redis` under Parameters.

Open `http://localhost:8081` once the resources are ready. If the backend services
started before the database schema was initialized, restart the API and Evaluation
services from the Aspire dashboard after completing initialization.
