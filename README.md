# FeatBit Aspire

FeatBit Aspire uses .NET Aspire to deploy FeatBit to Azure Container Apps and run it locally.

This branch targets [FeatBit v6.0.0-preview](https://github.com/featbit/featbit/releases/tag/6.0.0-preview) and supports only the Standalone (PostgreSQL) and Standard (PostgreSQL + Redis) deployment modes.

> [!IMPORTANT]
> This is a preview release for evaluation and feedback. Use a separate test environment
> and database; this branch does not migrate an existing v5 installation.

The version in `apphost.csproj` selects all three container images and the matching
PostgreSQL initialization files:

- `featbit/featbit-api-server:6.0.0-preview`
- `featbit/featbit-ui:6.0.0-preview`
- `featbit/featbit-evaluation-server:6.0.0-preview`

## Deploy to Azure Container Apps

> [!IMPORTANT]
> Follow the complete [Deploy to Azure Container Apps](docs/deploy-to-azure-container-apps.md)
> guide from start to finish.

## Run locally

Install the .NET 10 SDK, Aspire CLI 13.4.6 or later, and Docker or another compatible container runtime.

Run Standalone with PostgreSQL only:

PowerShell:

```powershell
$env:FeatBit__UseRedis = "false"
aspire run
```

Bash:

```bash
export FeatBit__UseRedis=false
aspire run
```

Run Standard with PostgreSQL and Redis:

PowerShell:

```powershell
$env:FeatBit__UseRedis = "true"
aspire run
```

Bash:

```bash
export FeatBit__UseRedis=true
aspire run
```

Open `http://localhost:8081` after the resources are ready.

On the first local run of a FeatBit version, the AppHost downloads that version's
PostgreSQL initialization files from the upstream FeatBit GitHub tag and caches them
under the Git-ignored `.aspire` directory. Later runs reuse the cached files.

Local PostgreSQL data persists in the `featbit-postgres-v6-preview-data` Docker volume,
separate from the v5 `featbit-postgres-data` volume. The preview starts with a fresh
database and preserves its data across restarts. Initialization files run only when
the database volume is empty; changing the image version does not migrate existing data.
