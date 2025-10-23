using IPSDatastreamWorker.Domain.Entities;

namespace IPSDatastreamWorker.Application.Common.Interfaces;

public interface IQualityCheckProcessor
{
    /// <summary>
    /// Processes quality check for a completed session
    /// </summary>
    Task ProcessSessionQualityAsync(Session session, CancellationToken cancellationToken = default);
}

