using System.Text.Json;
using IPSDatastreamWorker.Application.Common.DTOs;
using IPSDatastreamWorker.Application.Common.Interfaces;
using IPSDatastreamWorker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IPSDatastreamWorker.Application.Services;

public class QualityCheckProcessor : IQualityCheckProcessor
{
    private readonly IApplicationDbContext _context;
    private readonly IRedisCache _redisCache;
    private readonly ILogger<QualityCheckProcessor> _logger;

    // Quality scoring thresholds
    private const int MIN_DATA_POINTS = 70000;
    private const int MIN_DURATION_MINUTES = 5;
    private const decimal MIN_SENSOR_COVERAGE = 50.0m;  // 50% minimum coverage
    private const long MAX_GAP_MILLISECONDS = 3000;      // 3 second gap threshold
    
    // Anomaly detection thresholds (simplified for now)
    private const float ACCEL_MAX_THRESHOLD = 50.0f;     // m/s²
    private const float GYRO_MAX_THRESHOLD = 10.0f;      // rad/s
    private const float MAG_MAX_THRESHOLD = 200.0f;      // μT

    public QualityCheckProcessor(
        IApplicationDbContext context,
        IRedisCache redisCache,
        ILogger<QualityCheckProcessor> logger)
    {
        _context = context;
        _redisCache = redisCache;
        _logger = logger;
    }

    /// <summary>
    /// Normalizes timestamp to milliseconds. Handles timestamps with extra precision (nanoseconds).
    /// </summary>
    private long NormalizeTimestamp(long timestamp)
    {
        // If timestamp has more than 13 digits, it's likely in nanoseconds or has extra precision
        // Unix timestamp in milliseconds should be 13 digits (e.g., 1729747200000)
        // Example: 1761204205212000000 (19 digits) -> 1761204205212 (13 digits)
        
        if (timestamp > 9999999999999) // More than 13 digits
        {
            // Convert from nanoseconds (19 digits) to milliseconds (13 digits)
            // Divide by 1,000,000 to go from nanoseconds to milliseconds
            return timestamp / 1_000_000;
        }
        
        return timestamp;
    }

    public async Task ProcessSessionQualityAsync(Session session, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting quality check for session {SessionId}", session.SessionId);

            // 1. Fetch IMU data from Redis
            var imuDataList = await _redisCache.GetSessionDataAsync(session.SessionId, cancellationToken);
            
            if (imuDataList == null || !imuDataList.Any())
            {
                _logger.LogWarning("No IMU data found in Redis for session {SessionId}", session.SessionId);
                await MarkSessionAsFailed(session, "No IMU data found in cache", cancellationToken);
                return;
            }

            _logger.LogInformation("Retrieved {Count} IMU data points from Redis for session {SessionId}", 
                imuDataList.Count, session.SessionId);

            // 2. Fetch button presses from database
            var buttonPresses = await _context.ButtonPresses
                .AsNoTracking()
                .Where(bp => bp.SessionId == session.SessionId)
                .OrderBy(bp => bp.Timestamp)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Retrieved {Count} button presses from database for session {SessionId}", 
                buttonPresses.Count, session.SessionId);

            // Validate button presses exist
            if (!buttonPresses.Any())
            {
                _logger.LogWarning("No button presses found for session {SessionId}", session.SessionId);
                await MarkSessionAsFailed(session, "No button press events found - incomplete session data", cancellationToken);
                return;
            }

            // 3. Calculate quality metrics
            var metrics = CalculateQualityMetrics(session, imuDataList, buttonPresses);

            // 4. Update session with quality data (attach to context for tracking)
            _context.Sessions.Attach(session);
            UpdateSessionWithMetrics(session, metrics);

            // 5. Save changes
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("✓ Quality check completed successfully for session {SessionId} with score {Score:F2}", 
                session.SessionId, metrics.QualityScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing quality check for session {SessionId}", session.SessionId);
            
            // Try to mark session as failed
            try
            {
                await MarkSessionAsFailed(session, $"Quality check error: {ex.Message}", cancellationToken);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to mark session {SessionId} as failed", session.SessionId);
            }
        }
    }

    private QualityMetrics CalculateQualityMetrics(
        Session session, 
        List<IMUDataDto> imuDataList, 
        List<ButtonPress> buttonPresses)
    {
        var metrics = new QualityMetrics();
        var remarks = new List<string>();

        // ===== DATA VOLUME METRICS =====
        metrics.TotalIMUDataPoints = imuDataList.Count;
        metrics.TotalButtonPresses = buttonPresses.Count;

        // Calculate actual duration from session timestamps
        // Start time: When they reached society gate (ignore everything before)
        // End time: When session was closed (EndTimestamp)
        if (imuDataList.Any() && session.EndTimestamp.HasValue)
        {
            // Find the "REACHED_SOCIETY_GATE" button press to use as start time
            var reachedSocietyGate = buttonPresses
                .FirstOrDefault(bp => bp.Action == ButtonAction.ReachedSocietyGate);
            
            long startTimestamp;
            if (reachedSocietyGate != null)
            {
                // Start from when they reached society gate
                startTimestamp = NormalizeTimestamp(reachedSocietyGate.Timestamp);
                _logger.LogDebug("Using REACHED_SOCIETY_GATE timestamp as start: {Timestamp}", startTimestamp);
            }
            else
            {
                // Fallback to first IMU data point if no society gate button press found
                startTimestamp = NormalizeTimestamp(imuDataList.Min(d => d.Timestamp));
                _logger.LogWarning("No REACHED_SOCIETY_GATE button press found, using first IMU timestamp");
            }
            
            // Use session's EndTimestamp (when session was closed) as end time
            var endTimestamp = NormalizeTimestamp(session.EndTimestamp.Value);
            metrics.DurationMinutes = (endTimestamp - startTimestamp) / 1000.0 / 60.0; // Convert ms to minutes
            
            // Store start and end timestamps in additional metrics
            metrics.AdditionalMetrics["effective_start_timestamp"] = startTimestamp;
            metrics.AdditionalMetrics["effective_end_timestamp"] = endTimestamp;
            
            _logger.LogDebug("Session duration: {Duration} minutes (from {Start} to {End})", 
                metrics.DurationMinutes, startTimestamp, endTimestamp);
        }
        else
        {
            metrics.DurationMinutes = 0;
            _logger.LogWarning("Cannot calculate duration - missing IMU data or EndTimestamp");
        }

        // ===== SENSOR COVERAGE =====
        var totalDataPoints = imuDataList.Count;
        
        metrics.AccelDataCoverage = CalculateCoverage(imuDataList, d => d.AccelX.HasValue || d.AccelY.HasValue || d.AccelZ.HasValue);
        metrics.GyroDataCoverage = CalculateCoverage(imuDataList, d => d.GyroX.HasValue || d.GyroY.HasValue || d.GyroZ.HasValue);
        metrics.MagDataCoverage = CalculateCoverage(imuDataList, d => d.MagX.HasValue || d.MagY.HasValue || d.MagZ.HasValue);
        metrics.GpsDataCoverage = CalculateCoverage(imuDataList, d => d.Latitude.HasValue && d.Longitude.HasValue);
        metrics.BarometerDataCoverage = CalculateCoverage(imuDataList, d => d.Pressure.HasValue);

        // ===== DATA QUALITY FLAGS =====
        
        // Check for anomalies (sensor spikes)
        metrics.HasAnomalies = DetectAnomalies(imuDataList);
        
        // Check for data gaps (only after reaching society gate)
        var gapAnalysis = DetectDataGaps(imuDataList, buttonPresses);
        metrics.HasDataGaps = gapAnalysis.HasGaps;
        metrics.DataGapCount = gapAnalysis.GapCount;

        // ===== CALCULATE QUALITY SCORE =====
        decimal score = 100.0m;

        // Deduct points for insufficient data
        if (metrics.TotalIMUDataPoints < MIN_DATA_POINTS)
        {
            score -= 20;
            remarks.Add($"Insufficient data points ({metrics.TotalIMUDataPoints} < {MIN_DATA_POINTS})");
        }

        // Deduct points for short duration
        if (metrics.DurationMinutes < MIN_DURATION_MINUTES)
        {
            score -= 15;
            remarks.Add($"Session too short ({metrics.DurationMinutes:F1} min < {MIN_DURATION_MINUTES} min)");
        }

        // Deduct points for low sensor coverage
        if (metrics.AccelDataCoverage < MIN_SENSOR_COVERAGE)
        {
            score -= 15;
            remarks.Add($"Low accelerometer coverage ({metrics.AccelDataCoverage:F1}%)");
        }
        
        if (metrics.GyroDataCoverage < MIN_SENSOR_COVERAGE)
        {
            score -= 15;
            remarks.Add($"Low gyroscope coverage ({metrics.GyroDataCoverage:F1}%)");
        }

        if (metrics.MagDataCoverage < MIN_SENSOR_COVERAGE)
        {
            score -= 10;
            remarks.Add($"Low magnetometer coverage ({metrics.MagDataCoverage:F1}%)");
        }

        if (metrics.BarometerDataCoverage < MIN_SENSOR_COVERAGE)
        {
            score -= 10;
            remarks.Add($"Low barometer coverage ({metrics.BarometerDataCoverage:F1}%)");
        }

        if (metrics.GpsDataCoverage < 10) // GPS is less critical
        {
            score -= 5;
            remarks.Add($"Very low GPS coverage ({metrics.GpsDataCoverage:F1}%)");
        }

        // Deduct points for anomalies
        if (metrics.HasAnomalies)
        {
            score -= 10;
            remarks.Add("Sensor anomalies detected (spikes or unrealistic values)");
        }

        // Deduct points for data gaps
        if (metrics.HasDataGaps)
        {
            var gapPenalty = Math.Min(10, metrics.DataGapCount * 2); // Max 10 points deduction
            score -= gapPenalty;
            remarks.Add($"Data gaps detected ({metrics.DataGapCount} gaps > 1 second)");
        }

        // Bonus points for good button press data
        if (metrics.TotalButtonPresses >= 10)
        {
            score += 5;
            remarks.Add($"Good button press data ({metrics.TotalButtonPresses} events)");
        }

        // Ensure score is between 0 and 100
        score = Math.Max(0, Math.Min(100, score));
        
        metrics.QualityScore = score;
        metrics.QualityRemarks = remarks.Any() 
            ? string.Join("; ", remarks) 
            : "Quality check passed without issues";

        // Store additional metrics in JSON (with normalized timestamps)
        metrics.AdditionalMetrics["first_timestamp"] = imuDataList.Any() ? NormalizeTimestamp(imuDataList.Min(d => d.Timestamp)) : 0;
        metrics.AdditionalMetrics["last_timestamp"] = session.EndTimestamp.HasValue ? NormalizeTimestamp(session.EndTimestamp.Value) : 0;
        metrics.AdditionalMetrics["avg_accel_coverage"] = metrics.AccelDataCoverage;
        metrics.AdditionalMetrics["avg_gyro_coverage"] = metrics.GyroDataCoverage;
        metrics.AdditionalMetrics["checked_at"] = DateTime.UtcNow;

        return metrics;
    }

    private decimal CalculateCoverage(List<IMUDataDto> dataList, Func<IMUDataDto, bool> predicate)
    {
        if (!dataList.Any()) return 0;
        
        var count = dataList.Count(predicate);
        return (decimal)count / dataList.Count * 100.0m;
    }

    private bool DetectAnomalies(List<IMUDataDto> dataList)
    {
        foreach (var data in dataList)
        {
            // Check accelerometer values
            if (data.AccelX.HasValue && Math.Abs(data.AccelX.Value) > ACCEL_MAX_THRESHOLD) return true;
            if (data.AccelY.HasValue && Math.Abs(data.AccelY.Value) > ACCEL_MAX_THRESHOLD) return true;
            if (data.AccelZ.HasValue && Math.Abs(data.AccelZ.Value) > ACCEL_MAX_THRESHOLD) return true;

            // Check gyroscope values
            if (data.GyroX.HasValue && Math.Abs(data.GyroX.Value) > GYRO_MAX_THRESHOLD) return true;
            if (data.GyroY.HasValue && Math.Abs(data.GyroY.Value) > GYRO_MAX_THRESHOLD) return true;
            if (data.GyroZ.HasValue && Math.Abs(data.GyroZ.Value) > GYRO_MAX_THRESHOLD) return true;

            // Check magnetometer values
            if (data.MagX.HasValue && Math.Abs(data.MagX.Value) > MAG_MAX_THRESHOLD) return true;
            if (data.MagY.HasValue && Math.Abs(data.MagY.Value) > MAG_MAX_THRESHOLD) return true;
            if (data.MagZ.HasValue && Math.Abs(data.MagZ.Value) > MAG_MAX_THRESHOLD) return true;
        }

        return false;
    }

    private (bool HasGaps, int GapCount) DetectDataGaps(List<IMUDataDto> dataList, List<ButtonPress> buttonPresses)
    {
        if (dataList.Count < 2) return (false, 0);

        // Find the "REACHED_SOCIETY_GATE" timestamp
        var reachedSocietyGate = buttonPresses
            .FirstOrDefault(bp => bp.Action == ButtonAction.ReachedSocietyGate);
        
        long startCheckingFrom = 0;
        if (reachedSocietyGate != null)
        {
            // Only check for gaps AFTER reaching society gate (ignore gap from restaurant)
            startCheckingFrom = NormalizeTimestamp(reachedSocietyGate.Timestamp);
            _logger.LogDebug("Checking for data gaps only after REACHED_SOCIETY_GATE timestamp: {Timestamp}", startCheckingFrom);
        }
        else
        {
            _logger.LogWarning("No REACHED_SOCIETY_GATE found, checking all data for gaps");
        }

        // Filter data to only include records after society gate (or all if no gate timestamp)
        var relevantData = startCheckingFrom > 0
            ? dataList.Where(d => NormalizeTimestamp(d.Timestamp) >= startCheckingFrom).OrderBy(d => d.Timestamp).ToList()
            : dataList.OrderBy(d => d.Timestamp).ToList();

        if (relevantData.Count < 2) return (false, 0);

        int gapCount = 0;

        for (int i = 1; i < relevantData.Count; i++)
        {
            // Normalize timestamps to handle extra precision
            var currentTime = NormalizeTimestamp(relevantData[i].Timestamp);
            var previousTime = NormalizeTimestamp(relevantData[i - 1].Timestamp);
            var timeDiff = currentTime - previousTime;
            
            if (timeDiff > MAX_GAP_MILLISECONDS)
            {
                gapCount++;
                _logger.LogDebug("Data gap detected: {GapSeconds}s between records", timeDiff / 1000.0);
            }
        }

        return (gapCount > 0, gapCount);
    }

    private void UpdateSessionWithMetrics(Session session, QualityMetrics metrics)
    {
        session.QualityScore = metrics.QualityScore;
        session.QualityStatus = QualityCheckStatus.Completed;
        session.QualityCheckedAt = DateTime.UtcNow;
        session.QualityRemarks = metrics.QualityRemarks;

        // Data volume metrics
        session.TotalIMUDataPoints = metrics.TotalIMUDataPoints;
        session.TotalButtonPresses = metrics.TotalButtonPresses;
        session.DurationMinutes = metrics.DurationMinutes;

        // Sensor coverage
        session.AccelDataCoverage = metrics.AccelDataCoverage;
        session.GyroDataCoverage = metrics.GyroDataCoverage;
        session.MagDataCoverage = metrics.MagDataCoverage;
        session.GpsDataCoverage = metrics.GpsDataCoverage;
        session.BarometerDataCoverage = metrics.BarometerDataCoverage;

        // Quality flags
        session.HasAnomalies = metrics.HasAnomalies;
        session.HasDataGaps = metrics.HasDataGaps;
        session.DataGapCount = metrics.DataGapCount;

        // Store additional metrics as JSON
        session.QualityMetricsRawJson = JsonSerializer.Serialize(metrics.AdditionalMetrics);
        
        session.UpdatedAt = DateTime.UtcNow;
    }

    private async Task MarkSessionAsFailed(Session session, string reason, CancellationToken cancellationToken)
    {
        // Attach session to context if not already tracked
        if (_context.Sessions.Local.All(s => s.SessionId != session.SessionId))
        {
            _context.Sessions.Attach(session);
        }
        
        session.QualityStatus = QualityCheckStatus.Failed;
        session.QualityCheckedAt = DateTime.UtcNow;
        session.QualityRemarks = reason;
        session.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogWarning("Marked session {SessionId} as failed: {Reason}", session.SessionId, reason);
    }
}

