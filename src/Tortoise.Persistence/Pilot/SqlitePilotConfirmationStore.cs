using Microsoft.Data.Sqlite;
using Tortoise.Core.Pilot;
using Tortoise.Persistence.Migrations;
using Tortoise.Persistence.Options;

namespace Tortoise.Persistence.Pilot;

public sealed class SqlitePilotConfirmationStore : IPilotConfirmationStore, IDisposable
{
    private readonly TortoisePersistenceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    public SqlitePilotConfirmationStore(TortoisePersistenceOptions options)
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

    public async Task SaveAsync(
        PhysicalPilotConfirmationRecord record,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                INSERT INTO pilot_confirmations (
                    confirmation_id,
                    plan_id,
                    confirmed_at_utc,
                    confirmation_phrase,
                    acknowledged_item_ids_json,
                    checklist_ready,
                    summary)
                VALUES (
                    $confirmationId,
                    $planId,
                    $confirmedAtUtc,
                    $confirmationPhrase,
                    $acknowledgedItemIdsJson,
                    $checklistReady,
                    $summary)
                ON CONFLICT(confirmation_id) DO UPDATE SET
                    plan_id = excluded.plan_id,
                    confirmed_at_utc = excluded.confirmed_at_utc,
                    confirmation_phrase = excluded.confirmation_phrase,
                    acknowledged_item_ids_json = excluded.acknowledged_item_ids_json,
                    checklist_ready = excluded.checklist_ready,
                    summary = excluded.summary;
                """;

            command.Parameters.AddWithValue("$confirmationId", record.ConfirmationId.ToString());
            command.Parameters.AddWithValue("$planId", record.PlanId.ToString());
            command.Parameters.AddWithValue("$confirmedAtUtc", record.ConfirmedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$confirmationPhrase", record.ConfirmationPhrase);
            command.Parameters.AddWithValue(
                "$acknowledgedItemIdsJson",
                PilotConfirmationSerializer.SerializeAcknowledgements(record.AcknowledgedItemIds));
            command.Parameters.AddWithValue("$checklistReady", record.ChecklistReady ? 1 : 0);
            command.Parameters.AddWithValue("$summary", record.Summary);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PhysicalPilotConfirmationRecord?> GetLatestForPlanAsync(
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
                SELECT confirmation_id, plan_id, confirmed_at_utc, confirmation_phrase,
                       acknowledged_item_ids_json, checklist_ready, summary
                FROM pilot_confirmations
                WHERE plan_id = $planId
                ORDER BY confirmed_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$planId", planId.ToString());

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new PhysicalPilotConfirmationRecord(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                PilotConfirmationSerializer.DeserializeAcknowledgements(reader.GetString(4)),
                reader.GetInt32(5) == 1,
                reader.GetString(6));
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
}
