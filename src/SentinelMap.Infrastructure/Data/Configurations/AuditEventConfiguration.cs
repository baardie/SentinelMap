namespace SentinelMap.Infrastructure.Data.Configurations;

/// <summary>
/// Audit events table is created via raw SQL migration (partitioned, INSERT-only).
/// This configuration is excluded from EF model — see migration for schema.
/// </summary>
public static class AuditEventSchema
{
    public const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS audit_events (
            id            BIGINT GENERATED ALWAYS AS IDENTITY,
            timestamp     TIMESTAMPTZ NOT NULL DEFAULT now(),
            event_type    TEXT NOT NULL,
            user_id       UUID,
            action        TEXT NOT NULL,
            resource_type TEXT NOT NULL,
            resource_id   UUID,
            details       JSONB,
            ip_address    INET,
            PRIMARY KEY (id, timestamp)
        ) PARTITION BY RANGE (timestamp);
        """;
}
