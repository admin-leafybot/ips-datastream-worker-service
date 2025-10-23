using IPSDatastreamWorker.Application.Common.Interfaces;
using IPSDatastreamWorker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPSDatastreamWorker.Infrastructure.Services;

public class QualityCheckWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QualityCheckWorkerService> _logger;
    private readonly int _pollingIntervalSeconds;
    private readonly int _completedThresholdMinutes;
    private readonly int _batchSize;
    private readonly int _maxConcurrency;

    public QualityCheckWorkerService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<QualityCheckWorkerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Read configuration
        _pollingIntervalSeconds = configuration.GetValue<int>("QualityCheck:PollingIntervalSeconds", 30);
        _completedThresholdMinutes = configuration.GetValue<int>("QualityCheck:CompletedThresholdMinutes", 5);
        _batchSize = configuration.GetValue<int>("QualityCheck:BatchSize", 10);
        _maxConcurrency = configuration.GetValue<int>("QualityCheck:MaxConcurrency", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quality Check Worker Service starting up...");
        _logger.LogInformation("Polling Interval: {Interval} seconds", _pollingIntervalSeconds);
        _logger.LogInformation("Completed Threshold: {Threshold} minutes", _completedThresholdMinutes);
        _logger.LogInformation("Batch Size: {BatchSize}", _batchSize);
        _logger.LogInformation("Max Concurrency: {MaxConcurrency} parallel tasks", _maxConcurrency);

        // Wait a bit before starting to allow other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQualityChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in quality check worker service main loop");
            }

            // Wait before next polling cycle
            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Quality Check Worker Service shutting down...");
    }

    private async Task ProcessQualityChecksAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        try
        {
            // Calculate the threshold timestamp
            var thresholdTime = DateTime.UtcNow.AddMinutes(-_completedThresholdMinutes);
            var thresholdTimestamp = new DateTimeOffset(thresholdTime).ToUnixTimeMilliseconds();

            // Query for sessions that need quality checking
            // Criteria:
            // 1. Status = "completed"
            // 2. QualityStatus = 0 (pending) - only process new sessions, not already checked ones
            // 3. EndTimestamp is not null and > threshold (completed more than X minutes ago)
            // Using AsNoTracking for better performance since we're only reading
            var sessionsToCheck = await context.Sessions
                .AsNoTracking()
                .Where(s => 
                    s.Status == SessionStatus.Completed &&
                    s.QualityStatus == QualityCheckStatus.Pending &&
                    s.EndTimestamp.HasValue &&
                    s.EndTimestamp.Value < thresholdTimestamp)
                .OrderBy(s => s.EndTimestamp)
                .Take(_batchSize)
                .ToListAsync(stoppingToken);

            if (!sessionsToCheck.Any())
            {
                _logger.LogDebug("No sessions found for quality checking");
                return;
            }

            _logger.LogInformation("Found {Count} sessions for quality checking", sessionsToCheck.Count);

            // Process sessions in parallel with controlled concurrency
            await Parallel.ForEachAsync(
                sessionsToCheck,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxConcurrency,
                    CancellationToken = stoppingToken
                },
                async (session, token) =>
                {
                    // Create a new scope for each parallel task to ensure proper service lifetime
                    using var taskScope = _serviceProvider.CreateScope();
                    var taskProcessor = taskScope.ServiceProvider.GetRequiredService<IQualityCheckProcessor>();

                    try
                    {
                        _logger.LogInformation("Processing quality check for session {SessionId}", session.SessionId);
                        
                        await taskProcessor.ProcessSessionQualityAsync(session, token);
                        
                        _logger.LogInformation("✓ Completed quality check for session {SessionId}", session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing quality check for session {SessionId}", session.SessionId);
                        // Exception is logged but doesn't stop other parallel tasks
                    }
                });

            _logger.LogInformation("Completed parallel batch processing of {Count} sessions", sessionsToCheck.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessQualityChecksAsync");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Quality Check Worker Service stop requested");
        await base.StopAsync(cancellationToken);
    }
}

