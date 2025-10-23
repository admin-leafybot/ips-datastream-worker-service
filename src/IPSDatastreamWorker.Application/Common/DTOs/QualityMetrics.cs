namespace IPSDatastreamWorker.Application.Common.DTOs;

/// <summary>
/// Internal DTO for quality check calculations
/// </summary>
public class QualityMetrics
{
    public decimal QualityScore { get; set; }
    public string QualityRemarks { get; set; } = string.Empty;
    
    // Data Volume
    public int TotalIMUDataPoints { get; set; }
    public int TotalButtonPresses { get; set; }
    public double DurationMinutes { get; set; }
    
    // Sensor Coverage
    public decimal AccelDataCoverage { get; set; }
    public decimal GyroDataCoverage { get; set; }
    public decimal MagDataCoverage { get; set; }
    public decimal GpsDataCoverage { get; set; }
    public decimal BarometerDataCoverage { get; set; }
    
    // Quality Flags
    public bool HasAnomalies { get; set; }
    public bool HasDataGaps { get; set; }
    public int DataGapCount { get; set; }
    
    // Additional metrics for JSON storage
    public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
}

