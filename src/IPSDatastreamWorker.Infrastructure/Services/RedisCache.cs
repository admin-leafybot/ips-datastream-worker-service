using System.Text.Json;
using IPSDatastreamWorker.Application.Common.DTOs;
using IPSDatastreamWorker.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IPSDatastreamWorker.Infrastructure.Services;

public class RedisCache : IRedisCache, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisCache> _logger;
    private readonly string _keyPrefix;

    public RedisCache(IConfiguration configuration, ILogger<RedisCache> logger)
    {
        _logger = logger;
        
        var endpoint = configuration.GetValue<string>("Redis:Endpoint") 
            ?? throw new InvalidOperationException("Redis:Endpoint configuration not found.");
        var useSsl = configuration.GetValue<bool>("Redis:UseSsl");
        var database = configuration.GetValue<int>("Redis:Database");
        
        _keyPrefix = configuration.GetValue<string>("Redis:KeyPrefix") ?? "imu:session:";
        
        var options = ConfigurationOptions.Parse(endpoint);
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 3;
        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;
        options.Ssl = useSsl;
        options.DefaultDatabase = database;

        _redis = ConnectionMultiplexer.Connect(options);
        _db = _redis.GetDatabase();
        
        _logger.LogInformation("Connected to Redis at {Endpoint} (Database: {Database}, SSL: {UseSsl})", 
            endpoint, database, useSsl);
    }

    public async Task<List<IMUDataDto>> GetSessionDataAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{_keyPrefix}{sessionId}";
            
            _logger.LogDebug("Fetching session data from Redis with key: {Key}", key);
            
            // Get all items from the Redis list (acquisition worker stores as list, not string)
            var values = await _db.ListRangeAsync(key);
            
            if (values.Length == 0)
            {
                _logger.LogWarning("No data found in Redis for session {SessionId}", sessionId);
                return new List<IMUDataDto>();
            }

            var dataPoints = new List<IMUDataDto>();
            int deserializationFailures = 0;
            
            foreach (var value in values)
            {
                if (value.HasValue)
                {
                    try
                    {
                        var dataPoint = JsonSerializer.Deserialize<IMUDataDto>(value.ToString());
                        if (dataPoint != null)
                        {
                            dataPoints.Add(dataPoint);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        deserializationFailures++;
                        _logger.LogWarning(jsonEx, "Failed to deserialize data point #{FailureCount} for session {SessionId}. Raw value: {RawValue}", 
                            deserializationFailures, sessionId, value.ToString().Substring(0, Math.Min(100, value.ToString().Length)));
                        // Continue with other data points
                    }
                }
            }
            
            if (deserializationFailures > 0)
            {
                _logger.LogWarning("Total deserialization failures for session {SessionId}: {FailureCount} out of {TotalCount} records", 
                    sessionId, deserializationFailures, values.Length);
            }
            
            if (!dataPoints.Any())
            {
                _logger.LogWarning("No valid data points after deserialization for session {SessionId}", sessionId);
                return new List<IMUDataDto>();
            }

            _logger.LogInformation("Successfully retrieved {ProcessedCount} data points from Redis for session {SessionId} (Raw count: {RawCount}, Failures: {FailureCount})", 
                dataPoints.Count, sessionId, values.Length, deserializationFailures);
            
            return dataPoints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving session data from Redis for session {SessionId}", sessionId);
            return new List<IMUDataDto>();
        }
    }

    public async Task<bool> SessionExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{_keyPrefix}{sessionId}";
            // Check if list exists and has items
            var length = await _db.ListLengthAsync(key);
            return length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking session existence in Redis for session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task DeleteSessionDataAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{_keyPrefix}{sessionId}";
            await _db.KeyDeleteAsync(key);
            
            _logger.LogInformation("Deleted session data from Redis for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session data from Redis for session {SessionId}", sessionId);
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}

