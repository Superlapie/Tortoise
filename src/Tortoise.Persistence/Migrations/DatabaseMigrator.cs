using Microsoft.Data.Sqlite;

namespace Tortoise.Persistence.Migrations;

internal sealed class DatabaseMigrator
{
    public static void Migrate(SqliteConnection connection)
    {
        connection.Open();

        using var transaction = connection.BeginTransaction();
        var appliedVersions = GetAppliedVersions(connection, transaction);

        foreach (var (version, sql) in MigrationScripts.Scripts)
        {
            if (appliedVersions.Contains(version))
            {
                continue;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO schema_migrations (version, applied_at_utc)
                VALUES ($version, $appliedAtUtc);
                """;
            insert.Parameters.AddWithValue("$version", version);
            insert.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection connection, SqliteTransaction transaction)
    {
        var versions = new HashSet<int>();

        using var tableCheck = connection.CreateCommand();
        tableCheck.Transaction = transaction;
        tableCheck.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name = 'schema_migrations';
            """;

        if (tableCheck.ExecuteScalar() is null)
        {
            return versions;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_migrations;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }
}
