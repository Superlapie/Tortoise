namespace Tortoise.Persistence.Migrations;

internal static class MigrationScripts
{
    public static readonly IReadOnlyList<(int Version, string Sql)> Scripts =
    [
        (
            1,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS scan_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                scanned_at_utc TEXT NOT NULL,
                device_count INTEGER NOT NULL,
                problem_count INTEGER NOT NULL,
                recommended_count INTEGER NOT NULL,
                optional_count INTEGER NOT NULL,
                restricted_count INTEGER NOT NULL,
                unmatched_update_count INTEGER NOT NULL,
                warning_count INTEGER NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_scan_sessions_scanned_at_utc
                ON scan_sessions(scanned_at_utc DESC);
            """),
    ];
}
