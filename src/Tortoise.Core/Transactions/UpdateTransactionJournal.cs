using Tortoise.Core.Planning;

namespace Tortoise.Core.Transactions;

public static class UpdateTransactionJournal
{
    public static UpdateTransaction Advance(
        UpdateTransaction transaction,
        UpdateTransactionState to,
        IList<OperationJournalEntry> journal,
        string message,
        ref DateTimeOffset timestamp)
    {
        if (!UpdateTransactionTransitions.CanTransition(transaction.State, to))
        {
            throw new InvalidOperationException(
                $"Invalid transaction transition from '{transaction.State}' to '{to}'.");
        }

        timestamp = timestamp.AddMilliseconds(1);
        journal.Add(new OperationJournalEntry(
            transaction.TransactionId,
            transaction.State,
            to,
            timestamp,
            message));

        return transaction with { State = to, UpdatedAtUtc = timestamp };
    }

    public static async Task PersistAsync(
        IUpdateTransactionStore store,
        UpdateTransaction transaction,
        IList<OperationJournalEntry> journal,
        CancellationToken cancellationToken = default)
    {
        await store.SaveAsync(new UpdateTransactionRecord(transaction, journal.ToList()), cancellationToken);
    }
}
