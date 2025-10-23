namespace IPSDatastreamWorker.Domain.Entities;

public class Session
{
    // ===== CORE SESSION DATA =====
    public string SessionId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public long StartTimestamp { get; set; }
    public long? EndTimestamp { get; set; }
    public bool IsSynced { get; set; } = true;
    public string Status { get; set; } = SessionStatus.InProgress;
    public string PaymentStatus { get; set; } = PaymentStatusEnum.Unpaid;
    public string? Remarks { get; set; }
    public decimal? BonusAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ===== QUALITY SCORING =====
    public decimal? QualityScore { get; set; }              // 0-100 overall quality score
    public int QualityStatus { get; set; } = QualityCheckStatus.Pending;  // 0=pending, 1=completed, 2=failed
    public DateTime? QualityCheckedAt { get; set; }         // When quality check was run
    public string? QualityRemarks { get; set; }             // Human-readable quality issues
    
    // ===== DATA VOLUME METRICS =====
    public int? TotalIMUDataPoints { get; set; }            // Total IMU records for this session
    public int? TotalButtonPresses { get; set; }            // Total button press events
    public double? DurationMinutes { get; set; }            // Actual session duration in minutes
    
    // ===== SENSOR COVERAGE (% of records with each sensor) =====
    public decimal? AccelDataCoverage { get; set; }         // 0-100: % of records with accelerometer
    public decimal? GyroDataCoverage { get; set; }          // 0-100: % of records with gyroscope
    public decimal? MagDataCoverage { get; set; }           // 0-100: % of records with magnetometer
    public decimal? GpsDataCoverage { get; set; }           // 0-100: % of records with GPS
    public decimal? BarometerDataCoverage { get; set; }     // 0-100: % of records with barometer/pressure
    
    // ===== DATA QUALITY FLAGS =====
    public bool HasAnomalies { get; set; } = false;         // True if sensor spikes/anomalies detected
    public bool HasDataGaps { get; set; } = false;          // True if time gaps detected
    public int DataGapCount { get; set; } = 0;              // Number of data gaps (>1 second)
    
    // ===== FLEXIBLE JSON STORAGE =====
    public string? QualityMetricsRawJson { get; set; }      // JSONB for future metrics/ML features
}

public static class SessionStatus
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class PaymentStatusEnum
{
    public const string Unpaid = "unpaid";
    public const string Paid = "paid";
}

public static class QualityCheckStatus
{
    public const int Pending = 0;      // Quality check not yet performed
    public const int Completed = 1;    // Quality check completed successfully
    public const int Failed = 2;       // Quality check failed
}

