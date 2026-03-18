using SentinelMap.SharedKernel.DTOs;

namespace SentinelMap.SharedKernel.Interfaces;

public interface IAuditService
{
    /// <summary>
    /// Synchronous — blocks until persisted. Use for security events
    /// (auth failures, role changes, clearance modifications).
    /// </summary>
    Task WriteSecurityEventAsync(AuditEvent evt);

    /// <summary>
    /// Async via bounded channel — fire and forget. Use for operational events
    /// (entity viewed, alert acknowledged, geofence created).
    /// If the container crashes mid-flush, these may be lost — acceptable because
    /// the operation itself is already persisted.
    /// </summary>
    void WriteOperationalEvent(AuditEvent evt);
}
