using IPSDatastreamWorker.Application.Common.Interfaces;
using IPSDatastreamWorker.Application.Services;
using IPSDatastreamWorker.Infrastructure.Data;
using IPSDatastreamWorker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IPSDatastreamWorker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - Model First Approach (assumes tables already exist)
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Database connection string 'Default' not found.");
        
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                // Enable command batching for better bulk operation performance
                npgsqlOptions.MaxBatchSize(100);
                // Set command timeout for large operations
                npgsqlOptions.CommandTimeout(60);
            });
            options.UseSnakeCaseNamingConvention();
            
            // Performance optimizations
            options.EnableSensitiveDataLogging(false); // Disable in production
            options.EnableDetailedErrors(false); // Disable in production
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        
        // Redis Cache
        services.AddSingleton<IRedisCache, RedisCache>();
        
        // Application Services
        services.AddScoped<IQualityCheckProcessor, QualityCheckProcessor>();
        
        // Background Services
        services.AddHostedService<QualityCheckWorkerService>();

        return services;
    }
}

