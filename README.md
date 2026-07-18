# FeatBit Aspire

FeatBit Aspire uses .NET Aspire to deploy FeatBit to Azure Container Apps and run it locally.

This source tree targets [FeatBit v5.4.4](https://github.com/featbit/featbit/releases/tag/5.4.4) and supports only the Standalone (PostgreSQL) and Standard (PostgreSQL + Redis) deployment modes.

> Note: The FeatBit Aspire project is currently being rebuilt. The new implementation will support FeatBit v6.0.0 and later only.

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
