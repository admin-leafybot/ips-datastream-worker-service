using IPSDatastreamWorker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IPSDatastreamWorker.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Session> Sessions { get; set; }
    DbSet<ButtonPress> ButtonPresses { get; set; }
    DbSet<IMUData> IMUData { get; set; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

