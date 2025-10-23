# IPS Datastream Worker Service

A background worker service that performs quality checks on completed IPS (Indoor Positioning System) data collection sessions. Built with **Clean Architecture** principles using .NET 9.

## 🎯 Purpose

This worker service automatically processes completed sessions by:
1. **Monitoring** the database for sessions that have been completed for more than 5 minutes
2. **Fetching** IMU sensor data from Redis cache and button press events from PostgreSQL
3. **Analyzing** data quality, sensor coverage, gaps, and anomalies
4. **Calculating** comprehensive quality scores and metrics
5. **Updating** session records with quality assessment results

## 🏗️ Architecture

This project follows **Clean Architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────────────┐
│                  Worker Layer                       │
│            (Entry Point & Composition)              │
│  • Program.cs                                       │
│  • Configuration                                    │
└──────────────────┬──────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────┐
│             Infrastructure Layer                    │
│      (External Services & Persistence)              │
│  • Database (EF Core + PostgreSQL)                  │
│  • Redis Cache                                      │
│  • Background Worker Service                        │
│  • Dependency Injection                             │
└──────────────────┬──────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────┐
│             Application Layer                       │
│          (Business Logic & DTOs)                    │
│  • Quality Check Processor                          │
│  • Interfaces                                       │
│  • DTOs                                             │
└──────────────────┬──────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────┐
│              Domain Layer                           │
│         (Core Business Entities)                    │
│  • Session Entity                                   │
│  • ButtonPress Entity                               │
│  • IMUData Entity                                   │
│  • BaseEntity                                       │
└─────────────────────────────────────────────────────┘
```

## 📦 Projects

| Project | Purpose | Dependencies |
|---------|---------|--------------|
| **Domain** | Core business entities | None |
| **Application** | Business logic & interfaces | Domain |
| **Infrastructure** | Data access & external services | Application, Domain |
| **Worker** | Entry point & composition root | Infrastructure |

## 🚀 Getting Started

### Prerequisites

- .NET 9.0 SDK
- PostgreSQL 16+
- Redis 7+
- Docker & Docker Compose (optional)

### Local Development

1. **Clone the repository**
   ```bash
   cd ips-datastream-worker-service
   ```

2. **Update configuration**
   
   Edit `src/IPSDatastreamWorker.Worker/appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "Default": "Host=localhost;Port=5432;Database=ips_data_acquisition;Username=postgres;Password=postgres",
       "Redis": "localhost:6379"
     }
   }
   ```

3. **Build the solution**
   ```bash
   dotnet build
   ```

4. **Run the worker**
   ```bash
   cd src/IPSDatastreamWorker.Worker
   dotnet run
   ```

### Docker Development

1. **Start all services**
   ```bash
   docker-compose up -d
   ```

2. **View logs**
   ```bash
   docker-compose logs -f datastream-worker
   ```

3. **Stop services**
   ```bash
   docker-compose down
   ```

## ⚙️ Configuration

### Application Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `PollingIntervalSeconds` | How often to check for new sessions | 30 |
| `CompletedThresholdMinutes` | Minutes after completion before processing | 5 |
| `BatchSize` | Max sessions to process per cycle | 10 |

### Connection Strings

- **Default**: PostgreSQL database connection
- **Redis**: Redis cache connection

### Environment Variables

Production deployment can override settings via environment variables:

```bash
ConnectionStrings__Default="Host=prod-db;..."
ConnectionStrings__Redis="prod-redis:6379"
QualityCheck__PollingIntervalSeconds=30
QualityCheck__CompletedThresholdMinutes=5
QualityCheck__BatchSize=20
```

## 📊 Quality Check Process

### 1. Session Selection Criteria

Sessions are processed if they meet ALL criteria:
- Status = `"completed"`
- QualityStatus = `0` (pending) or `1` (reprocess)
- EndTimestamp > 5 minutes ago
- QualityCheckedAt = `null`

### 2. Data Collection

- **IMU Data**: Retrieved from Redis cache (key: `ips:session:{sessionId}`)
- **Button Presses**: Queried from PostgreSQL `button_presses` table

### 3. Quality Metrics Calculated

#### Data Volume
- Total IMU data points
- Total button presses
- Session duration (minutes)

#### Sensor Coverage (0-100%)
- Accelerometer coverage
- Gyroscope coverage
- Magnetometer coverage
- GPS coverage
- Barometer coverage

#### Quality Flags
- **HasAnomalies**: Sensor spikes or unrealistic values detected
- **HasDataGaps**: Time gaps > 1 second between data points
- **DataGapCount**: Number of gaps detected

#### Quality Score (0-100)
Starts at 100 and deducts points for:
- Insufficient data points (-20)
- Short duration (-15)
- Low sensor coverage (-15 per sensor)
- Anomalies detected (-10)
- Data gaps (-2 per gap, max -10)

Bonus points for:
- Good button press data (+5)

### 4. Results Storage

Updates the `sessions` table with:
- `quality_score`: 0-100 score
- `quality_status`: 1 (completed) or 2 (failed)
- `quality_checked_at`: Timestamp of check
- `quality_remarks`: Human-readable issues
- All metric fields (coverage, counts, flags)
- `quality_metrics_raw_json`: Additional metrics in JSON

## 🔍 Monitoring

### Logs

Structured logging with different levels:
- **Information**: Normal operations
- **Warning**: Recoverable issues (no data found)
- **Error**: Processing failures
- **Critical**: Fatal errors

### Key Log Messages

```
✓ Quality check completed successfully for session {SessionId} with score {Score}
⚠ No IMU data found in Redis for session {SessionId}
✗ Error processing quality check for session {SessionId}
```

## 🐳 Docker Deployment

### Development
```bash
docker-compose up -d
```

### Production
```bash
# Set environment variables
export DATABASE_CONNECTION_STRING="Host=..."
export REDIS_CONNECTION_STRING="..."

docker-compose -f docker-compose.prod.yml up -d
```

## 📈 Performance

### Optimizations

1. **Batch Processing**: Processes up to N sessions per cycle
2. **Efficient Queries**: Indexed database queries
3. **Connection Pooling**: EF Core automatic pooling
4. **Async Operations**: Fully async/await pattern

### Scalability

- **Horizontal Scaling**: Can run multiple instances
- **Stateless**: No shared state between workers
- **Idempotent**: Safe to reprocess sessions

## 🛠️ Development

### Adding New Quality Checks

1. Update `QualityMetrics` DTO in Application layer
2. Modify `CalculateQualityMetrics` method in `QualityCheckProcessor`
3. Add corresponding fields to `Session` entity if needed
4. Update database schema in API project

### Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true
```

## 📝 Related Projects

- **ips-data-acquisition-api**: API for session management (creates database schema)
- **ips-data-acquisition-worker-service**: Consumes RabbitMQ and stores IMU data
- **ips-data-acquisition-app**: Android app for data collection

## 📄 License

[Your License Here]

## 👥 Contributors

[Your Team]

## 🔗 Links

- [Architecture Documentation](ARCHITECTURE.md)
- [AWS Deployment Guide](AWS_DEPLOYMENT.md)
- [Getting Started Guide](GETTING_STARTED.md)

