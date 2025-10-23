# Getting Started Guide

## Prerequisites

Before you begin, ensure you have the following installed:

- ✅ **.NET 9.0 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- ✅ **PostgreSQL 16+** - [Download](https://www.postgresql.org/download/)
- ✅ **Redis 7+** - [Download](https://redis.io/download)
- ✅ **Docker & Docker Compose** (optional but recommended) - [Download](https://www.docker.com/products/docker-desktop)
- ✅ **Visual Studio 2022** or **Visual Studio Code** or **Rider**

## Quick Start with Docker

The fastest way to get started is using Docker Compose:

### 1. Clone and Navigate

```bash
cd ips-datastream-worker-service
```

### 2. Start Services

```bash
docker-compose up -d
```

This will start:
- PostgreSQL database
- Redis cache
- Datastream worker service

### 3. View Logs

```bash
docker-compose logs -f datastream-worker
```

### 4. Stop Services

```bash
docker-compose down
```

## Local Development Setup

### 1. Setup Database

Create PostgreSQL database:

```bash
createdb ips_data_acquisition
```

Or using PostgreSQL CLI:

```sql
CREATE DATABASE ips_data_acquisition;
```

**Note**: This service uses a model-first approach. The database schema should be created by the `ips-data-acquisition-api` project first.

### 2. Start Redis

#### macOS (Homebrew)
```bash
brew install redis
brew services start redis
```

#### Linux
```bash
sudo apt-get install redis-server
sudo systemctl start redis
```

#### Windows
Download and run from [Redis for Windows](https://github.com/microsoftarchive/redis/releases)

### 3. Configure Connection Strings

Edit `src/IPSDatastreamWorker.Worker/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=ips_data_acquisition;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "QualityCheck": {
    "PollingIntervalSeconds": 10,
    "CompletedThresholdMinutes": 5,
    "BatchSize": 5
  }
}
```

### 4. Build the Solution

```bash
dotnet build
```

### 5. Run the Worker

```bash
cd src/IPSDatastreamWorker.Worker
dotnet run
```

You should see output like:

```
info: Program[0]
      IPSDatastream Worker Service starting up...
info: Program[0]
      Environment: Development
info: QualityCheckWorkerService[0]
      Quality Check Worker Service starting up...
info: QualityCheckWorkerService[0]
      Polling Interval: 10 seconds
```

## Understanding the Workflow

### 1. Session Lifecycle

```
Session Created (API)
    ↓
IMU Data Stored (Acquisition Worker)
    ↓
Session Completed (API)
    ↓
Wait 5 minutes
    ↓
Quality Check (THIS SERVICE) ← You are here
    ↓
Session Updated with Quality Metrics
```

### 2. What This Service Does

1. **Polls Database**: Every N seconds (default: 30)
2. **Finds Sessions**: Status = "completed", quality_status = 0, completed > 5 min ago
3. **Fetches Data**: 
   - IMU data from Redis
   - Button presses from PostgreSQL
4. **Calculates Quality**:
   - Sensor coverage
   - Data gaps
   - Anomalies
   - Quality score (0-100)
5. **Updates Session**: Saves quality metrics to database

## Testing the Service

### 1. Create Test Data

You'll need to have the API and acquisition worker running to create sessions with data.

Alternatively, manually insert test data:

```sql
-- Insert a test session
INSERT INTO sessions (session_id, user_id, start_timestamp, end_timestamp, status, quality_status, created_at, updated_at)
VALUES ('test-session-123', 'user-456', 1729747200000, 1729750800000, 'completed', 0, NOW(), NOW());

-- Insert test button presses
INSERT INTO button_presses (session_id, user_id, action, timestamp, is_synced, created_at, updated_at)
VALUES 
  ('test-session-123', 'user-456', 'ENTERED_RESTAURANT_BUILDING', 1729747300000, true, NOW(), NOW()),
  ('test-session-123', 'user-456', 'REACHED_RESTAURANT', 1729748000000, true, NOW(), NOW());
```

Add test IMU data to Redis:

```bash
redis-cli
> SET ips:session:test-session-123 '[{"timestamp":1729747200000,"accelX":0.5,"accelY":0.3,"accelZ":9.8}]'
```

### 2. Trigger Processing

The service will automatically pick up the session in the next polling cycle (wait 10 seconds in dev mode).

### 3. Verify Results

Check the session was updated:

```sql
SELECT 
  session_id, 
  quality_status, 
  quality_score, 
  quality_remarks,
  total_imu_data_points,
  total_button_presses
FROM sessions 
WHERE session_id = 'test-session-123';
```

Expected result:
- `quality_status` = 1 (completed) or 2 (failed)
- `quality_score` = some value 0-100
- `quality_remarks` = details about the quality check

## Development Tips

### Hot Reload

The service supports hot reload for configuration changes:

```bash
dotnet watch run
```

### Debugging in Visual Studio

1. Open `IPSDatastreamWorker.sln`
2. Set `IPSDatastreamWorker.Worker` as startup project
3. Press F5 to debug

### Debugging in VS Code

Create `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET Core Launch (worker)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/IPSDatastreamWorker.Worker/bin/Debug/net9.0/IPSDatastreamWorker.Worker.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/IPSDatastreamWorker.Worker",
      "stopAtEntry": false,
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  ]
}
```

### Viewing Logs

Logs are written to console by default. For development, increase verbosity:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

### Testing Redis Connection

```bash
redis-cli ping
# Should return: PONG
```

### Testing Database Connection

```bash
psql -h localhost -U postgres -d ips_data_acquisition -c "SELECT COUNT(*) FROM sessions;"
```

## Configuration Options

### QualityCheck Settings

| Setting | Description | Default | Dev | Prod |
|---------|-------------|---------|-----|------|
| `PollingIntervalSeconds` | Time between database checks | 30 | 10 | 30 |
| `CompletedThresholdMinutes` | Wait time after completion | 5 | 5 | 5 |
| `BatchSize` | Max sessions per cycle | 10 | 5 | 20 |

### Connection Strings

| Connection | Format |
|------------|--------|
| PostgreSQL | `Host={host};Port={port};Database={db};Username={user};Password={pwd}` |
| Redis | `{host}:{port}` or `{host}:{port},password={pwd}` |

### Redis Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `KeyPrefix` | Prefix for Redis keys | `ips:session:` |

## Common Issues

### Issue: "Database connection failed"

**Solution**: Ensure PostgreSQL is running and connection string is correct.

```bash
# Check if PostgreSQL is running
pg_isready -h localhost -p 5432
```

### Issue: "Redis connection failed"

**Solution**: Ensure Redis is running.

```bash
# Check if Redis is running
redis-cli ping
```

### Issue: "No sessions found for quality checking"

**Solutions**:
1. Ensure sessions exist with `status = 'completed'`
2. Ensure `quality_status = 0`
3. Ensure `end_timestamp` is more than 5 minutes ago
4. Ensure `quality_checked_at` is NULL

### Issue: "No IMU data found in Redis"

**Solution**: Ensure the acquisition worker has stored data in Redis with the correct key format:
- Key: `ips:session:{sessionId}`
- Value: JSON array of IMU data points

## Next Steps

1. Read the [Architecture Documentation](ARCHITECTURE.md)
2. Review the [AWS Deployment Guide](AWS_DEPLOYMENT.md)
3. Explore the codebase
4. Run tests (when available)
5. Contribute improvements

## Support

For issues or questions:
- Check existing documentation
- Review logs for error messages
- Contact the development team

Happy coding! 🚀

