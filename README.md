# FeatBit Aspire Project

This is a comprehensive .NET Aspire application that demonstrates a microservices architecture with Azure cloud resources and Docker containers.

## Architecture

The project includes the following components:

### 🏗️ Infrastructure
- **Azure PostgreSQL Flexible Server** - Primary database
- **Azure Cache for Redis** - Caching and session storage

### 🚀 Services
- **Feature Flag Service** (.NET 8) - REST API for managing feature flags
- **User Service** (.NET 8) - REST API for user management
- **Angular UI** (Docker) - Frontend application using `featbit/featbit-ui:latest`
- **Python Analytics Service** (Docker) - FastAPI service for analytics and data processing

## Prerequisites

- **.NET 10 SDK** or later
- **Docker Desktop** (running)
- **Azure subscription** (for production deployment)
- **Visual Studio 2022** or **VS Code** with C# Dev Kit

## 🚀 Getting Started

### Local Development

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd featbit-aspire
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Run the Aspire AppHost**
   ```bash
   dotnet run --project FeatBit.AppHost
   ```

4. **Access the services:**
   - **Aspire Dashboard**: https://localhost:17106
   - **FeatBit Angular UI**: http://localhost:8081
   - **FeatBit Web API**: http://localhost:5000
   - **FeatBit Evaluation Server**: http://localhost:5100
   - **FeatBit Data Analytics**: http://localhost:8200

### 📊 Service Endpoints

#### FeatBit Web API Server (Port 5000)
- Main API server for feature flag management
- Handles user authentication and authorization
- Manages feature flag configurations and targeting rules
- Provides administrative interfaces

#### FeatBit Evaluation Server (Port 5100)
- High-performance feature flag evaluation engine
- Handles real-time feature flag evaluations
- Optimized for low-latency responses
- Used by client SDKs for flag evaluations

#### FeatBit Angular UI (Port 8081)
- Complete web-based management interface
- Feature flag dashboard and configuration
- User management and analytics
- Access via browser at http://localhost:8081

#### FeatBit Data Analytics Server (Port 8200)
- Analytics and reporting engine
- Data processing and insights
- Performance metrics and usage statistics

## 🐳 Docker Services

### FeatBit Web API Server
- **Image**: `featbitdocker/featbit-api-server:5.1.4`
- **Port**: 5000
- **Features**: ASP.NET Core API with PostgreSQL and Redis integration

### FeatBit Evaluation Server  
- **Image**: `featbitdocker/featbit-evaluation-server:5.1.4`
- **Port**: 5100
- **Features**: High-performance evaluation engine

### FeatBit Angular UI
- **Image**: `featbitdocker/featbit-ui:5.1.4`
- **Port**: 8081
- **Features**: Complete management interface

### FeatBit Data Analytics Server
- **Image**: `featbitdocker/featbit-data-analytics-server:latest`
- **Port**: 8200
- **Features**: Python-based analytics and reporting

## 🔧 Development

### Adding New Services

To add a new .NET service:
1. Create the service project
2. Add project reference in `FeatBit.AppHost.csproj`
3. Register in `AppHost.cs` with `AddProject<>()`

To add a new Docker service:
1. Use `AddContainer()` for existing images
2. Use `AddDockerfile()` for custom builds

### Environment Configuration

Services automatically receive connection strings for:
- PostgreSQL via `postgres.Resource.ConnectionStringExpression`
- Redis via `redis.Resource.ConnectionStringExpression`

## 🚀 Deployment to Azure

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
   
   During deployment, you'll be prompted to:
   - Select your Azure subscription
   - Choose a region (e.g., `West US 2`)
   - Enter PostgreSQL connection string
   - Enter Redis connection string
   
   The deployment will:
   - Create a resource group (e.g., `rg-featbit-aspire-env`)
   - Create an Azure Container Apps Environment
   - Create an Azure Container Registry
   - Create Application Insights for monitoring
   - Deploy all 4 container services with 3 replicas each for high availability
   - Configure HTTPS endpoints automatically

5. **Access your deployed application**
   
   After successful deployment, you'll see:
   ```
   - FeatBit UI: https://featbit-ui.<environment>.azurecontainerapps.io/
   - FeatBit API: https://featbit-api.<environment>.azurecontainerapps.io/
   - Evaluation Server: https://featbit-evaluation-server.<environment>.azurecontainerapps.io/
   - Aspire Dashboard: https://aspire-dashboard.ext.<environment>.azurecontainerapps.io
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