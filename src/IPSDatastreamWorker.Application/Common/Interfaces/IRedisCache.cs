using IPSDatastreamWorker.Application.Common.DTOs;

namespace IPSDatastreamWorker.Application.Common.Interfaces;

public interface IRedisCache
{
    /// <summary>
    /// Retrieves IMU data for a specific session from Redis cache
    /// </summary>
    Task<List<IMUDataDto>> GetSessionDataAsync(string sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if session data exists in Redis
    /// </summary>
    Task<bool> SessionExistsAsync(string sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes session data from Redis after processing (optional cleanup)
    /// </summary>
    Task DeleteSessionDataAsync(string sessionId, CancellationToken cancellationToken = default);
}

