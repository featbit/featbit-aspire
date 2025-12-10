# 🏠 Local Development Monitoring Guide

## ✅ Configured Local Development Mode

### 📋 **Development Environment Configuration** (`appsettings.Development.json`)

```json
{
  "LocalDevelopment": {
    "DisableApplicationInsights": true,    // 🚫 Disable Azure Application Insights
    "UseLocalTelemetry": true             // ✅ Enable local telemetry
  },
  "UseExisting": {
    "ApplicationInsights": false          // Don't use existing AI resources
  }
}
```

## 🔍 **Local Monitoring Methods**

### **Method 1: Aspire Dashboard** (Recommended)
- **URL**: `http://localhost:15888` (opens automatically after running the app)
- **Features**: 
  - 🚀 Real-time service status
  - 📊 Resource usage
  - 📝 Live log streams
  - 🔗 Inter-service dependencies
  - 🌡️ Health check status

### **Method 2: Console Output**
- **OpenTelemetry Data**: Direct output to console
- **Structured Logs**: Include trace ID, timestamps, service names
- **Metrics Data**: CPU, memory, request statistics

### **Method 3: Local OTLP Endpoint** (Optional)
```bash
# If you want to use tools like Jaeger, configure OTLP endpoint
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

## 🎯 **Local Monitoring Advantages**

✅ **No Azure Account Required**: Runs completely locally  
✅ **Real-time Feedback**: Immediate log and metrics viewing  
✅ **Zero Cost**: No Azure charges  
✅ **Offline Development**: No network connection needed  
✅ **Debug Friendly**: Console output makes troubleshooting easy  

## 🚀 **Running the Application**

```bash
# Start local development environment
dotnet run --project FeatBit.AppHost

# Aspire Dashboard opens automatically
# URL: http://localhost:15888
```

## 📊 **Data Available Locally**

### **Service Monitoring**:
- 🟢 Service running status (Running/Stopped/Failed)
- 🔄 Container restart count
- 💾 Memory and CPU usage
- 🌐 Network ports and connection status

### **Log Data**:
- 📝 Structured application logs
- ⚠️ Error and warning information
- 🔍 OpenTelemetry trace data
- 📈 HTTP request/response logs

### **Dependencies**:
- 🔗 PostgreSQL connection status
- 🔗 Redis connection status
- 🔗 Inter-service communication status

## 🔄 **Production Environment Switch**

When deploying to production, simply modify the configuration:

```json
{
  "LocalDevelopment": {
    "DisableApplicationInsights": false,   // ✅ Enable Azure Application Insights
    "UseLocalTelemetry": false
  },
  "ConnectionStrings": {
    "ApplicationInsights": "InstrumentationKey=your-key;..."
  }
}
```

## 💡 **Development Tips**

1. **Real-time Monitoring**: Keep Aspire Dashboard open to view service status in real-time
2. **Log Viewing**: Use console output for quick troubleshooting
3. **Performance Testing**: Local environment can test basic performance metrics
4. **Debug Mode**: All OpenTelemetry data outputs to console

Now you can enjoy a completely localized development experience with comprehensive monitoring capabilities without needing to connect to Azure! 🎉