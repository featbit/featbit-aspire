# FeatBit Aspire

FeatBit Aspire uses .NET Aspire to run FeatBit locally and deploy it to Azure Container Apps.

This source tree targets [FeatBit v5.4.4](https://github.com/featbit/featbit/releases/tag/5.4.4) and supports only the Standalone (PostgreSQL) and Standard (PostgreSQL + Redis) deployment modes.

> Note: The FeatBit Aspire project is currently being rebuilt. The new implementation will support FeatBit v6.0.0 and later only.

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

## Deploy to Azure Container Apps

The Azure deployment runs the FeatBit UI, API, and Evaluation services in Azure Container Apps. It requires an initialized external PostgreSQL database and, for Standard mode, an external Redis service.

Configure the required deployment values, sign in to Azure, and deploy:

PowerShell:

```powershell
$env:FeatBit__Azure__MinReplicas = "1"
$env:FeatBit__Azure__MaxReplicas = "10"
az login
aspire deploy --apphost ./apphost.csproj --environment Production
```

Bash:

```bash
export FeatBit__Azure__MinReplicas=1
export FeatBit__Azure__MaxReplicas=10
az login
aspire deploy --apphost ./apphost.csproj --environment Production
```

Set both values to the same number for a fixed replica count, or use a range to enable automatic scaling for all three Container Apps.

See [Deploy to Azure Container Apps](docs/deployment.md) for the required parameters, secrets, database initialization, topology selection, and CI/CD usage.
