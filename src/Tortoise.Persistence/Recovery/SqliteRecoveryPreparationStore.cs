using Microsoft.Data.Sqlite;
using Tortoise.Core.Recovery;
using Tortoise.Persistence.Migrations;
using Tortoise.Persistence.Options;
using Tortoise.Persistence.Serialization;

namespace Tortoise.Persistence.Recovery;

public sealed class SqliteRecoveryPreparationStore : IRecoveryPreparationStore, IDisposable
{
    private readonly TortoisePersistenceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    public SqliteRecoveryPreparationStore(TortoisePersistenceOptions options)
    {
        _options = options;
    }

    public async Task SaveAsync(
        RecoveryPreparationRecord record,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                INSERT INTO recovery_preparations (
                    preparation_id,
                    plan_id,
                    transaction_id,
                    prepared_at_utc,
                    device_instance_id,
                    export_succeeded,
                    system_restore_enabled,
                    payload_json)
                VALUES (
                    $preparationId,
                    $planId,
                    $transactionId,
                    $preparedAtUtc,
                    $deviceInstanceId,
                    $exportSucceeded,
                    $systemRestoreEnabled,
                    $payloadJson)
                ON CONFLICT(preparation_id) DO UPDATE SET
                    plan_id = excluded.plan_id,
                    transaction_id = excluded.transaction_id,
                    prepared_at_utc = excluded.prepared_at_utc,
                    device_instance_id = excluded.device_instance_id,
                    export_succeeded = excluded.export_succeeded,
                    system_restore_enabled = excluded.system_restore_enabled,
                    payload_json = excluded.payload_json;
                """;

            command.Parameters.AddWithValue("$preparationId", record.PreparationId.ToString());
            command.Parameters.AddWithValue("$planId", record.PlanId.ToString());
            command.Parameters.AddWithValue(
                "$transactionId",
                record.TransactionId.HasValue ? record.TransactionId.Value.ToString() : DBNull.Value);
            command.Parameters.AddWithValue("$preparedAtUtc", record.PreparedAtUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$deviceInstanceId",
                record.Snapshot.BeforeDevice.Identity.DeviceInstanceId);
            command.Parameters.AddWithValue("$exportSucceeded", record.ExportAttempt.Succeeded ? 1 : 0);
            command.Parameters.AddWithValue("$systemRestoreEnabled", record.SystemRestore.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$payloadJson", RecoveryPreparationSerializer.Serialize(record));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryPreparationRecord?> GetAsync(
        Guid preparationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                SELECT payload_json
                FROM recovery_preparations
                WHERE preparation_id = $preparationId;
                """;
            command.Parameters.AddWithValue("$preparationId", preparationId.ToString());

            var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
            return payload is null ? null : RecoveryPreparationSerializer.Deserialize(payload);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RecoveryPreparationSummary>> ListAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                planId is null
                    ? """
                      SELECT payload_json
                      FROM recovery_preparations
                      ORDER BY prepared_at_utc DESC;
                      """
                    : """
                      SELECT payload_json
                      FROM recovery_preparations
                      WHERE plan_id = $planId
                      ORDER BY prepared_at_utc DESC;
                      """;

            if (planId is not null)
            {
                command.Parameters.AddWithValue("$planId", planId.Value.ToString());
            }

            var summaries = new List<RecoveryPreparationSummary>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var record = RecoveryPreparationSerializer.Deserialize(reader.GetString(0));
                summaries.Add(new RecoveryPreparationSummary(
                    record.PreparationId,
                    record.PlanId,
                    record.PreparedAtUtc,
                    record.Snapshot.BeforeDevice.Identity.FriendlyName,
                    record.ExportAttempt.Succeeded,
                    record.SystemRestore.IsEnabled));
            }

            return summaries;
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
        if (_initialized)
        {
            return;
        }

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
}
