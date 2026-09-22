namespace Tortoise.Core.Mutation;

public sealed class MutationWorkflowLock : IDisposable
{
    private const string MutexName = "Global\\Tortoise.Mutation.Workflow";
    private readonly Mutex? _mutex;
    private readonly bool _acquired;

    private MutationWorkflowLock(Mutex? mutex, bool acquired)
    {
        _mutex = mutex;
        _acquired = acquired;
    }

    public static MutationWorkflowLock TryAcquire()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MutationWorkflowLock(null, true);
        }

        var mutex = new Mutex(initiallyOwned: false, MutexName, out _);
        var acquired = mutex.WaitOne(TimeSpan.Zero);
        return new MutationWorkflowLock(mutex, acquired);
    }

    public bool IsAcquired => _acquired;

    public void Dispose()
    {
        if (_acquired && _mutex is not null)
        {
            _mutex.ReleaseMutex();
        }

        _mutex?.Dispose();
    }
}
