# FeatBit Aspire - Deploy FeatBit to Azure with .NET Aspire

Deploy [FeatBit](https://github.com/featbit/featbit) - an open-source feature flag management platform - to Azure using .NET Aspire.

## 🏗️ Architecture

**Services:**
- **FeatBit Web API** - Main API server for feature flag management
- **FeatBit Evaluation Server** - High-performance evaluation engine
- **FeatBit UI** - Angular-based web interface
- **FeatBit Data Analytics** - Analytics and reporting service

**Infrastructure:**
- **Database** - PostgreSQL or MongoDB (configurable)
- **Cache** - Redis
- **Monitoring** - Azure Application Insights (when deployed to Azure)

## ⚙️ Prerequisites

- **Azure subscription**
- **Azure Developer CLI (azd)** - [Install guide](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- **PostgreSQL or MongoDB database** - Create a database instance and run FeatBit database initialization scripts:
  - For PostgreSQL: Run the [PostgreSQL init script](https://github.com/featbit/featbit/tree/main/modules/back-end/src/Infrastructure/Store/Dbs/PostgreSql)
  - For MongoDB: Run the [MongoDB init script](https://github.com/featbit/featbit/tree/main/modules/back-end/src/Infrastructure/Store/Dbs/MongoDb)
- **Redis instance** - Set up a Redis cache instance (local or cloud)

## 🚀 Deploy to Azure

### 1. Clone Repository

```bash
git clone https://github.com/featbit/featbit-aspire.git
cd featbit-aspire
```

### 2. Configure Database and Redis

Edit `FeatBit.AppHost/appsettings.json` with your database and Redis connection strings:

**For PostgreSQL:**
```json
{
  "DbProvider": "Postgres",
  "ConnectionStrings": {
    "Postgres": "Host=yourhost;Database=featbit;Username=user;Password=pass;Port=5432",
    "Redis": "yourhost:6379,password=yourpassword,ssl=True"
  }
}
```

**For MongoDB:**
```json
{
  "DbProvider": "MongoDb",
  "ConnectionStrings": {
    "MongoDb": "mongodb://yourhost:27017/featbit",
    "Redis": "yourhost:6379,password=yourpassword,ssl=True"
  },
  "MongoDb": {
    "Database": "featbit",
    "Host": "mongodb"
  }
}
```

### 3. Login to Azure

```bash
azd auth login
```

A browser window will open for Azure authentication. Sign in with your Azure account.

### 4. Initialize and Deploy

```bash
azd up
```

This command will initialize and deploy in one step. During the process, you'll be prompted for several inputs:

**Prompt 1: Environment Name**
```
? Enter a new environment name: [? for help]
```
- Enter a unique name (e.g., `featbit-prod`, `featbit-dev`)
- This will be used in resource naming (e.g., `rg-featbit-prod`)
- Use lowercase letters, numbers, and hyphens only

**Prompt 2: Azure Subscription** (if you have multiple subscriptions)
```
? Select an Azure Subscription to use:
  1. Subscription 1 (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
> 2. Subscription 2 (yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy)
  3. Subscription 3 (zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz)
```
- Use arrow keys to select your subscription
- Press Enter to confirm

**Prompt 3: Azure Region**
```
? Select an Azure location to use:
  1. (US) East US (eastus)
> 2. (US) West US 2 (westus2)
  3. (Europe) West Europe (westeurope)
  4. (Asia Pacific) Southeast Asia (southeastasia)
  ...
```
- Select the region closest to your users
- Common choices: `eastus`, `westus2`, `westeurope`, `southeastasia`
- Press Enter to confirm

**Deployment Progress:**

After providing these inputs, the deployment begins:

```bash
Initializing project...

Provisioning Azure resources (azd provision)
Provisioning Azure resources can take some time

  (✓) Done: Resource group: rg-featbit-prod
  (✓) Done: Container Apps Environment: featbit-env
  (✓) Done: Log Analytics workspace
  (✓) Done: Application Insights

Deploying services (azd deploy)

  (✓) Done: Deploying service featbit-ui
  - Endpoint: https://featbit-ui.xxx.eastus.azurecontainerapps.io
  
  (✓) Done: Deploying service featbit-api
  - Endpoint: https://featbit-api.xxx.eastus.azurecontainerapps.io
  
  (✓) Done: Deploying service featbit-evaluation-server
  - Endpoint: https://featbit-evaluation-server.xxx.eastus.azurecontainerapps.io

SUCCESS: Your application was deployed to Azure in 12 minutes.
```

**⏱️ Expected Time:** 10-15 minutes total

### 5. Access Your Deployment

After deployment completes, note the endpoints displayed:

- **FeatBit UI**: `https://featbit-ui.<random>.azurecontainerapps.io`
- **FeatBit Web API**: `https://featbit-api.<random>.azurecontainerapps.io`
- **FeatBit Evaluation Server**: `https://featbit-evaluation-server.<random>.azurecontainerapps.io`

**Default Login:**
- Email: `test@featbit.com`
- Password: `123456`

## 🔄 Update Deployment

When you need to update your deployed application (e.g., upgrading to a new FeatBit version), use:

```bash
azd deploy
```

This command is sufficient for most updates, including:
- **Upgrading Docker image versions** in AppHost.cs (e.g., `featbit/featbit-api-server:5.1.4` → `5.2.0`)
- **Changing environment variables** in appsettings.json
- **Updating configuration** settings

### Deployment Process

When you run `azd deploy`, it will detect changes and prompt for confirmation:

```bash
$ azd deploy

? The following services will be updated. Continue? (Y/n)
  - featbit-api
  - featbit-ui
```

Press `Y` to confirm and proceed with deployment.

**Deployment output:**

```bash
Deploying services (azd deploy)

  (✓) Done: Deploying service featbit-api
  - Endpoint: https://featbit-api.xxx.eastus.azurecontainerapps.io
  
  (✓) Done: Deploying service featbit-ui
  - Endpoint: https://featbit-ui.xxx.eastus.azurecontainerapps.io

SUCCESS: Your application was deployed to Azure in 3 minutes 45 seconds.
```

**What gets updated:**
- Modified container images are pulled and deployed
- Updated services are redeployed with zero downtime
- New environment variables (if changed in appsettings.json)
- Health checks verify services are running correctly

**If no changes are detected:**

```bash
$ azd deploy

  (✓) Done: Service featbit-api is up to date
  (✓) Done: Service featbit-ui is up to date

SUCCESS: All services are up to date.
```

### When to Use `azd up` Instead

Use `azd up` only when you've made **infrastructure-level changes**:

```bash
azd up
```

**Examples requiring `azd up`:**
- **Adding or removing services** in AppHost.cs
- **Changing scaling rules** (min/max replicas, CPU/memory limits)
- **Modifying network configuration** (ports, ingress settings)
- **Updating Container Apps Environment settings**

**Note:** For most day-to-day updates (like upgrading FeatBit versions), `azd deploy` is faster and sufficient.

**Quick Reference:**
- **Upgrading FeatBit versions** (changing image tags) → Use `azd deploy` (⏱️ ~3-5 min)
- **Adding/removing services or changing infrastructure** → Use `azd up` (⏱️ ~10-15 min)

## 📊 Monitor Deployment

```bash
# View all resources and endpoints
azd show

# View logs
azd logs

# Open Application Insights
azd monitor
```

## 🧹 Clean Up

Remove all Azure resources:

```bash
azd down
```

You'll be prompted to confirm:
```
? Total resources to delete: 6, are you sure you want to continue? (y/N)
```

Type `y` and press Enter to delete all resources.

## 📋 Azure Resources Created

The deployment creates:
- **Azure Container Apps Environment** - Managed hosting environment
- **Application Insights** - Monitoring and telemetry
- **Log Analytics Workspace** - Centralized logging
- **4 Container Apps** (each with 3-10 replicas):
  - `featbit-api` - Web API Server (external HTTPS)
  - `featbit-evaluation-server` - Evaluation Server (external HTTPS)
  - `featbit-ui` - Angular UI (external HTTPS)
  - `featbit-da-server` - Data Analytics (internal only)

## 🔧 Troubleshooting

**Deployment fails:**
```bash
# Run with debug output
azd deploy --debug

# Check authentication
azd auth login
az account show
```

**Update connection strings after deployment:**
```bash
az containerapp update \
  --name featbit-api \
  --resource-group rg-<your-env-name> \
  --set-env-vars \
    "Postgres__ConnectionString=<your-connection>" \
    "Redis__ConnectionString=<your-redis-connection>"
```

**View logs:**
```bash
# All services
azd logs

# Specific service
azd logs --service featbit-api
```

## 📚 Resources

- [FeatBit Documentation](https://docs.featbit.co)
- [FeatBit GitHub](https://github.com/featbit/featbit)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)

## 📄 License

This project follows the same license as FeatBit. See the [LICENSE](LICENSE) file for details.