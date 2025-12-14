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

**Local Development:**
- .NET 10 SDK or later
- Docker Desktop (running)
- PostgreSQL or MongoDB instance
- Redis instance

**Azure Deployment:**
- Azure subscription
- Azure Developer CLI (azd)

## 🚀 Local Development

### 1. Clone and Configure

```bash
git clone https://github.com/featbit/featbit-aspire.git
cd featbit-aspire
```

### 2. Configure Connection Strings

Edit `FeatBit.AppHost/appsettings.Development.json`:

**For PostgreSQL:**
```json
{
  "DbProvider": "Postgres",
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=featbit;Username=postgres;Password=yourpassword;Port=5432",
    "Redis": "localhost:6379"
  }
}
```

**For MongoDB:**
```json
{
  "DbProvider": "MongoDb",
  "ConnectionStrings": {
    "MongoDb": "mongodb://localhost:27017/featbit",
    "Redis": "localhost:6379"
  },
  "MongoDb": {
    "Database": "featbit",
    "Host": "mongodb"
  }
}
```

### 3. Run the Application

```bash
dotnet run --project FeatBit.AppHost
```

### 4. Access Services

- **Aspire Dashboard**: https://localhost:17106
- **FeatBit UI**: http://localhost:8081
- **FeatBit Web API**: http://localhost:5000
- **FeatBit Evaluation Server**: http://localhost:5100
- **FeatBit Data Analytics**: http://localhost:8200

**Default Login:**
- Email: `test@featbit.com`
- Password: `123456`


## ☁️ Deployment to Azure

### Prerequisites for Azure Deployment

- **Azure subscription** with appropriate permissions
- **Azure Developer CLI (azd)** installed
- **Existing PostgreSQL and Redis** instances (connection strings configured in `appsettings.json`)

### Initial Deployment

1. **Install Azure Developer CLI**
   ```bash
   # Windows (using winget)
   winget install microsoft.azd
   
   # Or download from https://aka.ms/azd-install
   ```

2. **Login to Azure**
   ```bash
   azd auth login
   ```

3. **Initialize the project**
   ```bash
   azd init --from-code
   ```
   
   When prompted:
   - Select `.NET (Aspire)` as the detected service
   - Enter a unique environment name (e.g., `featbit-aspire-env`)
   - Confirm to continue

4. **Deploy to Azure**
   ```bash
   azd up
   ```
   


### Updating Your Deployment

After making code changes, update your Azure deployment:

1. **Quick update (code changes only)**
   ```bash
   azd deploy
   ```
   This redeploys your services without reprovisioning infrastructure.

2. **Full update (code + infrastructure changes)**
   ```bash
   azd up
   ```
   Use this when you've modified `AppHost.cs` configuration.

3. **Update specific service**
   ```bash
   azd deploy <service-name>
   # Example: azd deploy featbit-api
   ```

### Managing Your Azure Resources

**View deployment status:**
```bash
azd show
```

**View environment details:**
```bash
azd env get-values
```

**Monitor logs:**
```bash
azd monitor
```

**Delete all Azure resources:**
```bash
azd down --force --purge
```

Or using Azure CLI:
```bash
az group delete --name rg-featbit-aspire-env --yes --no-wait
```

### Architecture in Azure

The deployment creates:

- **Azure Container Apps Environment** - Managed Kubernetes environment
- **Azure Container Registry** - Private container image storage
- **Application Insights** - Monitoring and telemetry
- **Log Analytics Workspaces** - Centralized logging
- **4 Container Apps** (each with 3 replicas for high availability):
  - `featbit-api` - Web API Server (external HTTPS)
  - `featbit-evaluation-server` - Evaluation Server (external HTTPS)
  - `featbit-ui` - Angular UI (external HTTPS)
  - `featbit-da-server` - Data Analytics (internal only)

### Production Configuration

The application automatically detects publish mode and:
- Uses explicit ports (5000, 5100, 8081) for **local development**
- Uses automatic port assignment (port 80) for **Azure deployment**
- Configures HTTPS endpoints for external services in Azure
- Sets up internal-only access for the Data Analytics service
- Configures environment variables with connection strings
- Enables Application Insights for monitoring
- Scales services with 3-10 replicas for high availability

### Cost Optimization

To reduce costs in development:
- Modify replica counts in `AppHost.cs`:
  ```csharp
  containerApp.Template.Scale.MinReplicas = 1;
  containerApp.Template.Scale.MaxReplicas = 3;
  ```
- Delete resources when not in use: `azd down`
- Use Azure Cost Management to monitor spending

## 🔍 Monitoring

The Aspire Dashboard provides:
- **Service health** and status
- **Logs** from all services
- **Metrics** and telemetry
- **Distributed tracing**
- **Resource usage**

## 🛠️ Troubleshooting

### Common Issues

1. **Docker not running**
   - Ensure Docker Desktop is started
   - Check container status in Aspire Dashboard

2. **Port conflicts**
   - Modify port assignments in `AppHost.cs`
   - Use `WithHttpEndpoint(hostPort: <new-port>)`

3. **Azure connection issues**
   - Verify Azure CLI authentication: `az login`
   - Check Azure subscription permissions

### Logs

Access logs through:
- **Aspire Dashboard** - Real-time logs for all services
- **Docker Desktop** - Container logs
- **Azure Portal** - Production logs (when deployed)

## 📁 Project Structure

```
featbit-aspire/
├── FeatBit.AppHost/           # Aspire orchestration
├── FeatBit.ServiceDefaults/   # Shared service configuration
├── FeatBit.FeatureFlagService/ # Feature flag REST API
├── FeatBit.UserService/       # User management REST API
├── python-app/                # Python analytics service
│   ├── main.py
│   ├── requirements.txt
│   └── Dockerfile
└── README.md
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes and test locally
4. Submit a pull request

## 📝 License

This project is licensed under the MIT License - see the LICENSE file for details.