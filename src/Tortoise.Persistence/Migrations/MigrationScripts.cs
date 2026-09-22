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
        (
            2,
            """
            CREATE TABLE IF NOT EXISTS update_plans (
                plan_id TEXT PRIMARY KEY,
                scan_session_id INTEGER,
                device_instance_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                classification TEXT NOT NULL,
                risk_level TEXT NOT NULL,
                is_frozen INTEGER NOT NULL,
                plan_hash TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                FOREIGN KEY (scan_session_id) REFERENCES scan_sessions(id)
            );

            CREATE INDEX IF NOT EXISTS ix_update_plans_scan_session_id
                ON update_plans(scan_session_id);

            CREATE TABLE IF NOT EXISTS update_transactions (
                transaction_id TEXT PRIMARY KEY,
                plan_id TEXT NOT NULL,
                state TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                journal_json TEXT NOT NULL,
                FOREIGN KEY (plan_id) REFERENCES update_plans(plan_id)
            );

            CREATE INDEX IF NOT EXISTS ix_update_transactions_plan_id
                ON update_transactions(plan_id);
            """),
    ];
}
