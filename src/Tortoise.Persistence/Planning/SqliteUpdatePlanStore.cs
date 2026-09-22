using Microsoft.Data.Sqlite;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;
using Tortoise.Persistence.Migrations;
using Tortoise.Persistence.Options;
using Tortoise.Persistence.Serialization;

namespace Tortoise.Persistence.Planning;

public sealed class SqliteUpdatePlanStore : IUpdatePlanStore, IDisposable
{
    private readonly TortoisePersistenceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    public SqliteUpdatePlanStore(TortoisePersistenceOptions options)
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

            EnsureConnection();
            await Task.Run(() => DatabaseMigrator.Migrate(_connection!), cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePlansAsync(
        IReadOnlyList<StoredUpdatePlan> plans,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var transaction = _connection!.BeginTransaction();

            foreach (var storedPlan in plans)
            {
                if (storedPlan.IsFrozen)
                {
                    using var existsCommand = _connection.CreateCommand();
                    existsCommand.Transaction = transaction;
                    existsCommand.CommandText =
                        """
                        SELECT is_frozen
                        FROM update_plans
                        WHERE plan_id = $planId;
                        """;
                    existsCommand.Parameters.AddWithValue("$planId", storedPlan.PlanId.ToString());
                    var existing = await existsCommand.ExecuteScalarAsync(cancellationToken);
                    if (existing is not null && Convert.ToInt32(existing) == 1)
                    {
                        throw new InvalidOperationException(
                            $"Plan '{storedPlan.PlanId}' is frozen and cannot be overwritten.");
                    }
                }

                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO update_plans (
                        plan_id,
                        scan_session_id,
                        device_instance_id,
                        created_at_utc,
                        classification,
                        risk_level,
                        is_frozen,
                        plan_hash,
                        payload_json)
                    VALUES (
                        $planId,
                        $scanSessionId,
                        $deviceInstanceId,
                        $createdAtUtc,
                        $classification,
                        $riskLevel,
                        $isFrozen,
                        $planHash,
                        $payloadJson)
                    ON CONFLICT(plan_id) DO UPDATE SET
                        scan_session_id = excluded.scan_session_id,
                        device_instance_id = excluded.device_instance_id,
                        created_at_utc = excluded.created_at_utc,
                        classification = excluded.classification,
                        risk_level = excluded.risk_level,
                        is_frozen = excluded.is_frozen,
                        plan_hash = excluded.plan_hash,
                        payload_json = excluded.payload_json
                    WHERE is_frozen = 0;
                    """;

                command.Parameters.AddWithValue("$planId", storedPlan.PlanId.ToString());
                command.Parameters.AddWithValue(
                    "$scanSessionId",
                    storedPlan.ScanSessionId.HasValue ? storedPlan.ScanSessionId.Value : DBNull.Value);
                command.Parameters.AddWithValue(
                    "$deviceInstanceId",
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId);
                command.Parameters.AddWithValue("$createdAtUtc", storedPlan.CreatedAtUtc.ToString("O"));
                command.Parameters.AddWithValue("$classification", storedPlan.Classification.ToString());
                command.Parameters.AddWithValue("$riskLevel", storedPlan.RiskLevel.ToString());
                command.Parameters.AddWithValue("$isFrozen", storedPlan.IsFrozen ? 1 : 0);
                command.Parameters.AddWithValue("$planHash", storedPlan.Plan.PlanHash);
                command.Parameters.AddWithValue("$payloadJson", UpdatePlanSerializer.Serialize(storedPlan.Plan));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredUpdatePlan?> GetPlanAsync(
        Guid planId,
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
                    plan_id,
                    scan_session_id,
                    created_at_utc,
                    classification,
                    risk_level,
                    is_frozen,
                    payload_json
                FROM update_plans
                WHERE plan_id = $planId;
                """;
            command.Parameters.AddWithValue("$planId", planId.ToString());

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadPlan(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
        long? scanSessionId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                scanSessionId is null
                    ? """
                      SELECT
                          plan_id,
                          scan_session_id,
                          created_at_utc,
                          classification,
                          risk_level,
                          is_frozen,
                          payload_json
                      FROM update_plans
                      ORDER BY created_at_utc DESC;
                      """
                    : """
                      SELECT
                          plan_id,
                          scan_session_id,
                          created_at_utc,
                          classification,
                          risk_level,
                          is_frozen,
                          payload_json
                      FROM update_plans
                      WHERE scan_session_id = $scanSessionId
                      ORDER BY created_at_utc DESC;
                      """;

            if (scanSessionId is not null)
            {
                command.Parameters.AddWithValue("$scanSessionId", scanSessionId.Value);
            }

            var plans = new List<StoredUpdatePlan>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                plans.Add(ReadPlan(reader));
            }

            return plans;
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

    private void EnsureConnection()
    {
        if (_connection is not null)
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
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private static StoredUpdatePlan ReadPlan(SqliteDataReader reader)
    {
        var planId = Guid.Parse(reader.GetString(0));
        var scanSessionId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
        var createdAtUtc = DateTimeOffset.Parse(reader.GetString(2));
        var classification = Enum.Parse<UpdateClassification>(reader.GetString(3));
        var riskLevel = Enum.Parse<DriverRiskLevel>(reader.GetString(4));
        var isFrozen = reader.GetInt32(5) == 1;
        var plan = UpdatePlanSerializer.Deserialize(reader.GetString(6));

        return new StoredUpdatePlan(
            planId,
            scanSessionId,
            createdAtUtc,
            classification,
            riskLevel,
            isFrozen,
            plan);
    }
}
