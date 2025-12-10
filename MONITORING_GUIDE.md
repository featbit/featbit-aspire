# FeatBit Data Analytics Service Monitoring Guide

## 📊 Configured Monitoring Options

### 1. **Application Insights Integration**
```bash
# Environment variables configured:
APPLICATIONINSIGHTS_CONNECTION_STRING=<connection_string>
OTEL_SERVICE_NAME=featbit-data-analytics
OTEL_RESOURCE_ATTRIBUTES=service.name=featbit-data-analytics,service.version=1.0.0
```

### 2. **OpenTelemetry Support**
- Automatic telemetry data collection
- Distributed tracing
- Custom metrics and logging

### 3. **Log Monitoring**
```bash
LOG_LEVEL=INFO                    # Log level control
ENABLE_METRICS=true              # Enable metrics collection
HEALTH_CHECK_PATH=/health        # Health check endpoint
```

## 🔍 Monitoring Methods

### **Method 1: Azure Application Insights**
View through Azure Portal:
- **Live Metrics**: Real-time CPU, memory, request count
- **Application Map**: Service dependency relationships
- **Failure Analysis**: Error and exception tracking
- **Performance**: Response time and throughput

### **Method 2: Aspire Dashboard**
Access Aspire Dashboard during local development:
```bash
# Access after running the application
http://localhost:15888
```
View content:
- Container status and resource usage
- Live log streams
- Environment variables and configuration
- Network connection status

### **Method 3: Container-level Monitoring**
```bash
# If using Docker, monitor containers
docker stats featbit-da-server
docker logs featbit-da-server -f
```

### **Method 4: Azure Container Apps Monitoring**
In Azure Portal:
- **Metrics**: CPU, memory, replica count, request count
- **Logs**: Container logs and application logs
- **Diagnostics**: Performance issue diagnosis
- **Alerts**: Custom alert rules

## 🎯 Python Application Monitoring Best Practices

### **Recommended additions to Python application:**

1. **OpenTelemetry Python SDK**:
```python
# requirements.txt
opentelemetry-api
opentelemetry-sdk
opentelemetry-instrumentation-flask  # or other frameworks
azure-monitor-opentelemetry
```

2. **Health Check Endpoints**:
```python
@app.route('/health')
def health_check():
    return {'status': 'healthy', 'timestamp': datetime.utcnow().isoformat()}

@app.route('/ready')  
def readiness_check():
    # Check database connections, etc.
    return {'status': 'ready', 'database': 'connected'}
```

3. **Custom Metrics**:
```python
from opentelemetry import metrics

# Create custom metrics
meter = metrics.get_meter(__name__)
request_counter = meter.create_counter("requests_total")
processing_time = meter.create_histogram("processing_duration")
```

## 📈 Recommended Monitoring Metrics

### **Key Metrics**:
- **Request Count**: Total HTTP requests and success rate
- **Processing Time**: Data processing latency
- **Error Rate**: Exception and failed request ratio
- **Resource Usage**: CPU and memory utilization
- **Database Performance**: Query time and connection count

### **Business Metrics**:
- **Data Processing Volume**: Number of events/feature flag evaluations processed
- **User Activity**: Active users and session count
- **Feature Flag Usage**: Usage frequency of various flags

## 🚨 Recommended Alert Configuration

Set up in Application Insights:
- CPU usage > 80%
- Memory usage > 85%
- Error rate > 5%
- Response time > 2000ms
- Availability < 99%

## 📊 Recommended Dashboard

Create a monitoring dashboard containing:
1. **Service Health Status** overview
2. **Request volume and response time** trends
3. **Error rate and exception** details
4. **Resource usage** charts
5. **Dependent services** status (PostgreSQL, Redis)