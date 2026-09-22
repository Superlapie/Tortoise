namespace Tortoise.Contracts.Mutation;

public sealed class MutationDeniedException : Exception
{
    public MutationDeniedException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
