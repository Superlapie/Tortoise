using Microsoft.Data.Sqlite;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;
using Tortoise.Persistence.Migrations;
using Tortoise.Persistence.Options;
using Tortoise.Persistence.Serialization;

namespace Tortoise.Persistence.ScanSessions;

public sealed class SqliteScanSessionStore : IScanSessionStore, IDisposable
{
    private readonly TortoisePersistenceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    public SqliteScanSessionStore(TortoisePersistenceOptions options)
    {
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var databasePath = _options.DatabasePath ?? TortoiseDatabasePaths.GetDefaultDatabasePath();
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());

            await Task.Run(() => DatabaseMigrator.Migrate(_connection), cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScanSessionRecord> SaveAsync(
        RecommendationScanResult result,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var summary = ScanSessionSummaryCalculator.Calculate(result);
            var payload = RecommendationScanResultSerializer.Serialize(result);

            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                INSERT INTO scan_sessions (
                    scanned_at_utc,
                    device_count,
                    problem_count,
                    recommended_count,
                    optional_count,
                    restricted_count,
                    unmatched_update_count,
                    warning_count,
                    payload_json)
                VALUES (
                    $scannedAtUtc,
                    $deviceCount,
                    $problemCount,
                    $recommendedCount,
                    $optionalCount,
                    $restrictedCount,
                    $unmatchedUpdateCount,
                    $warningCount,
                    $payloadJson);
                SELECT last_insert_rowid();
                """;

            command.Parameters.AddWithValue("$scannedAtUtc", result.EvaluatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$deviceCount", summary.DeviceCount);
            command.Parameters.AddWithValue("$problemCount", summary.ProblemCount);
            command.Parameters.AddWithValue("$recommendedCount", summary.RecommendedCount);
            command.Parameters.AddWithValue("$optionalCount", summary.OptionalCount);
            command.Parameters.AddWithValue("$restrictedCount", summary.RestrictedCount);
            command.Parameters.AddWithValue("$unmatchedUpdateCount", summary.UnmatchedUpdateCount);
            command.Parameters.AddWithValue("$warningCount", summary.WarningCount);
            command.Parameters.AddWithValue("$payloadJson", payload);

            var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return new ScanSessionRecord(id, result.EvaluatedAtUtc, summary, result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScanSessionRecord?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await ListAsync(limit: 1, cancellationToken);
        return sessions.Count == 0 ? null : sessions[0];
    }

    public async Task<ScanSessionRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                SELECT
                    id,
                    scanned_at_utc,
                    device_count,
                    problem_count,
                    recommended_count,
                    optional_count,
                    restricted_count,
                    unmatched_update_count,
                    warning_count,
                    payload_json
                FROM scan_sessions
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", id);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadRecord(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ScanSessionRecord>> ListAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                SELECT
                    id,
                    scanned_at_utc,
                    device_count,
                    problem_count,
                    recommended_count,
                    optional_count,
                    restricted_count,
                    unmatched_update_count,
                    warning_count,
                    payload_json
                FROM scan_sessions
                ORDER BY scanned_at_utc DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);

            var records = new List<ScanSessionRecord>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(ReadRecord(reader));
            }

            return records;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _gate.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private static ScanSessionRecord ReadRecord(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var scannedAtUtc = DateTimeOffset.Parse(reader.GetString(1));
        var summary = new ScanSessionSummary(
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8));
        var payload = reader.GetString(9);
        var result = RecommendationScanResultSerializer.Deserialize(payload);

        return new ScanSessionRecord(id, scannedAtUtc, summary, result);
    }
}
