# Architecture Documentation

## Overview

The IPS Datastream Worker Service is built using **Clean Architecture** principles, ensuring maintainability, testability, and separation of concerns. This service performs automated quality checks on completed IPS data collection sessions.

## Clean Architecture Layers

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

## Layer Details

### 1. Domain Layer (`IPSDatastreamWorker.Domain`)

**Purpose**: Contains core business entities and domain logic.

**Key Components**:
- `BaseEntity.cs` - Base class for all entities with common properties (Id, CreatedAt, UpdatedAt)
- `Session.cs` - Entity representing a data collection session with quality metrics
- `ButtonPress.cs` - Entity representing user button press events during a session
- `IMUData.cs` - Entity representing IMU sensor data points

**Characteristics**:
- ✅ No dependencies on other layers
- ✅ Pure business logic
- ✅ Framework-agnostic
- ✅ Highly testable

**Design Decisions**:
- All sensor fields are nullable - not all devices have all sensors
- Follows the same schema as the API project for consistency
- Uses `long` for primary keys and timestamps
- Includes comprehensive quality tracking fields

### 2. Application Layer (`IPSDatastreamWorker.Application`)

**Purpose**: Contains application business logic, DTOs, and interfaces.

**Key Components**:

#### DTOs
- `IMUDataDto` - DTO for IMU data retrieved from Redis
- `QualityMetrics` - Internal DTO for quality calculations

#### Interfaces
- `IApplicationDbContext` - Database context abstraction
- `IRedisCache` - Redis cache abstraction
- `IQualityCheckProcessor` - Quality check processor abstraction

#### Services
- `QualityCheckProcessor` - Core business logic for quality checking
  - Fetches IMU data from Redis
  - Fetches button presses from database
  - Calculates quality metrics
  - Updates session with results
  - Handles errors gracefully

**Characteristics**:
- ✅ Depends only on Domain layer
- ✅ Defines interfaces (not implementations)
- ✅ Contains no infrastructure concerns
- ✅ Easily unit testable with mocks

**Quality Scoring Algorithm**:
```csharp
// Base score: 100
// Deductions:
// - Insufficient data points: -20
// - Short duration: -15
// - Low accelerometer coverage: -15
// - Low gyroscope coverage: -15
// - Very low GPS coverage: -5
// - Anomalies detected: -10
// - Data gaps: -2 per gap (max -10)
// Bonuses:
// + Good button press data: +5
// Final score: max(0, min(100, score))
```

### 3. Infrastructure Layer (`IPSDatastreamWorker.Infrastructure`)

**Purpose**: Implements external services and data persistence.

**Key Components**:

#### Data
- `ApplicationDbContext` - EF Core DbContext implementation
  - Implements `IApplicationDbContext`
  - Configures entity relationships
  - Auto-updates `UpdatedAt` on entity changes
  - **Model-First Approach** - No migrations

#### Services
- `RedisCache` - Redis cache implementation
  - Connects to Redis using StackExchange.Redis
  - Retrieves session IMU data
  - JSON serialization/deserialization
  - Error handling and logging

- `QualityCheckWorkerService` - Background service
  - Extends `BackgroundService`
  - Polls database every N seconds
  - Processes sessions in batches
  - Creates scoped services for each batch
  - Graceful shutdown support

#### Configuration
- `DependencyInjection.cs` - Service registration
  - Registers DbContext with PostgreSQL
  - Registers Redis cache
  - Registers application services
  - Registers background worker

**Characteristics**:
- ✅ Depends on Application and Domain layers
- ✅ Contains all external dependencies
- ✅ Implements interfaces defined in Application layer
- ✅ Handles all I/O operations

**Design Decisions**:
- **Model-First Database**: Does not create or manage migrations. Assumes tables exist (created by API project)
- **Scoped Services**: Creates new scope for each processing cycle to ensure proper lifetime management
- **Error Handling**: Gracefully handles missing data and processing errors

### 4. Worker Layer (`IPSDatastreamWorker.Worker`)

**Purpose**: Application entry point and composition root.

**Key Components**:
- `Program.cs` - Application bootstrap and dependency injection setup
- `appsettings.json` - Configuration files for different environments

**Characteristics**:
- ✅ Depends on Infrastructure layer only
- ✅ Composes all layers
- ✅ Configures logging and hosting
- ✅ Environment-specific configuration

## Design Patterns

### 1. Dependency Inversion Principle (DIP)

High-level modules (Application) don't depend on low-level modules (Infrastructure). Both depend on abstractions (interfaces).

```csharp
// Application layer defines the interface
public interface IQualityCheckProcessor
{
    Task ProcessSessionQualityAsync(string sessionId, CancellationToken cancellationToken);
}

// Infrastructure layer implements it
public class QualityCheckProcessor : IQualityCheckProcessor
{
    // Implementation
}
```

### 2. Repository Pattern (via DbContext)

EF Core's `DbContext` acts as a Unit of Work and `DbSet<T>` as repositories.

### 3. Background Service Pattern

`QualityCheckWorkerService` extends `BackgroundService` for long-running background tasks.

### 4. Factory Pattern (Service Provider)

Uses `IServiceProvider` to create scoped services for each processing cycle.

```csharp
using (var scope = _serviceProvider.CreateScope())
{
    var processor = scope.ServiceProvider.GetRequiredService<IQualityCheckProcessor>();
    await processor.ProcessSessionQualityAsync(sessionId, stoppingToken);
}
```

## Processing Flow

```
┌─────────────────────┐
│   Timer Trigger     │
│  (Every N seconds)  │
└──────┬──────────────┘
       │
       ▼
┌──────────────────────────────┐
│ QualityCheckWorkerService    │
│ 1. Query sessions needing    │
│    quality check             │
└──────┬───────────────────────┘
       │
       ▼
┌──────────────────────────────┐
│   QualityCheckProcessor      │
│ 2. Fetch IMU data from Redis │
│ 3. Fetch button presses      │
└──────┬───────────────────────┘
       │
       ▼
┌──────────────────────────────┐
│   Calculate Quality Metrics  │
│ 4. Sensor coverage           │
│ 5. Data gaps                 │
│ 6. Anomaly detection         │
│ 7. Quality score             │
└──────┬───────────────────────┘
       │
       ▼
┌──────────────────────────────┐
│    Update Session Record     │
│ 8. Save quality metrics      │
│ 9. Mark as completed/failed  │
└──────────────────────────────┘
```

## Database Strategy

### Model-First Approach

This worker service uses a **Model-First** approach:

1. **Domain entities** (Session, ButtonPress, IMUData) are defined in code
2. **No migrations** are created or managed by this service
3. **Database schema** is assumed to already exist (created by the API project)
4. **DbContext** is configured to NOT create/update database

**Benefits**:
- ✅ Single source of truth for schema (API project)
- ✅ Prevents accidental schema changes
- ✅ Faster startup (no migration checks)
- ✅ Clear separation of responsibilities

## Error Handling Strategy

### Processing Errors

1. **Missing IMU Data**
   - Log warning
   - Mark session as failed (quality_status = 2)
   - Record reason in quality_remarks

2. **Missing Session**
   - Log warning
   - Skip processing

3. **Processing Errors**
   - Log error with exception
   - Mark session as failed
   - Continue with next session

4. **Fatal Errors** (Database connection lost)
   - Log error
   - Service stops
   - Kubernetes/Docker will restart

### Session Status Flow

```
quality_status = 0 (Pending)
       │
       ▼
   Process ──✓──> quality_status = 1 (Completed)
       │
       └──✗──> quality_status = 2 (Failed)
```

## Performance Considerations

### Polling Strategy

- Configurable polling interval (default: 30 seconds)
- Batch processing (default: 10 sessions per cycle)
- Small delay between sessions (100ms) to avoid overwhelming

### Database Optimization

- Composite indexes on (status, quality_status)
- Efficient WHERE clauses
- Connection pooling via EF Core

### Redis Optimization

- Connection multiplexer singleton
- Connection retry and timeout settings
- Async operations throughout

## Scalability

### Horizontal Scaling

Multiple worker instances can run simultaneously:
- Each polls database independently
- Database ensures no duplicate processing via `quality_checked_at`
- No shared state between workers

### Configuration for Scale

Increase `BatchSize` for higher throughput:
```json
{
  "QualityCheck": {
    "BatchSize": 50
  }
}
```

Decrease `PollingIntervalSeconds` for lower latency:
```json
{
  "QualityCheck": {
    "PollingIntervalSeconds": 10
  }
}
```

## Logging Strategy

Structured logging with different levels:

- **Information**: Normal operations (session processed, batch completed)
- **Warning**: Recoverable issues (no data found, session not found)
- **Error**: Processing failures (exceptions, data errors)
- **Critical**: Fatal errors (not currently used)

## Testing Strategy

### Unit Tests
- Application layer services (mock `IApplicationDbContext`, `IRedisCache`)
- Domain entity behavior
- Quality scoring algorithm

### Integration Tests
- Database operations (in-memory or test PostgreSQL)
- Redis cache operations (test Redis instance)
- End-to-end quality check flow

### Load Tests
- Multiple concurrent processing cycles
- Large batch sizes
- High-frequency polling

## Future Enhancements

1. **Retry Logic** - Retry failed sessions after N hours
2. **Metrics/Telemetry** - Export Prometheus metrics for monitoring
3. **Advanced Anomaly Detection** - ML-based anomaly detection
4. **Health Checks** - HTTP endpoint for liveness/readiness probes
5. **Distributed Locking** - Prevent duplicate processing in multi-instance deployments
6. **Priority Queue** - Process high-priority sessions first
7. **Webhook Notifications** - Notify external systems when quality check completes

