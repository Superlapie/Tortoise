using Microsoft.Data.Sqlite;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Persistence.Migrations;
using Tortoise.Persistence.Options;
using Tortoise.Persistence.Serialization;

namespace Tortoise.Persistence.Planning;

public sealed class SqliteUpdateTransactionStore : IUpdateTransactionStore, IDisposable
{
    private readonly TortoisePersistenceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    public SqliteUpdateTransactionStore(TortoisePersistenceOptions options)
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

    public async Task<UpdateTransactionRecord> SaveAsync(
        UpdateTransactionRecord transaction,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                INSERT INTO update_transactions (
                    transaction_id,
                    plan_id,
                    state,
                    updated_at_utc,
                    journal_json)
                VALUES (
                    $transactionId,
                    $planId,
                    $state,
                    $updatedAtUtc,
                    $journalJson)
                ON CONFLICT(transaction_id) DO UPDATE SET
                    plan_id = excluded.plan_id,
                    state = excluded.state,
                    updated_at_utc = excluded.updated_at_utc,
                    journal_json = excluded.journal_json;
                """;

            command.Parameters.AddWithValue("$transactionId", transaction.Transaction.TransactionId.ToString());
            command.Parameters.AddWithValue("$planId", transaction.Transaction.PlanId.ToString());
            command.Parameters.AddWithValue("$state", transaction.Transaction.State.ToString());
            command.Parameters.AddWithValue("$updatedAtUtc", transaction.Transaction.UpdatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$journalJson",
                UpdateTransactionSerializer.SerializeJournal(transaction.Journal));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return transaction;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UpdateTransactionRecord?> GetAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                """
                SELECT transaction_id, plan_id, state, updated_at_utc, journal_json
                FROM update_transactions
                WHERE transaction_id = $transactionId;
                """;
            command.Parameters.AddWithValue("$transactionId", transactionId.ToString());

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadTransaction(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UpdateTransactionRecord?> GetLatestForPlanAsync(
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
                SELECT transaction_id, plan_id, state, updated_at_utc, journal_json
                FROM update_transactions
                WHERE plan_id = $planId
                ORDER BY updated_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$planId", planId.ToString());

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadTransaction(reader);
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

    private static UpdateTransactionRecord ReadTransaction(SqliteDataReader reader)
    {
        var transaction = new UpdateTransaction(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Enum.Parse<UpdateTransactionState>(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)));
        var journal = UpdateTransactionSerializer.DeserializeJournal(reader.GetString(4));

        return new UpdateTransactionRecord(transaction, journal);
    }
}
