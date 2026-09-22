namespace Tortoise.Core.Mutation;

public sealed class MutationServicingLock : IDisposable
{
    private const string MutexName = "Global\\Tortoise.Mutation.Servicing";
    private readonly Mutex? _mutex;
    private readonly bool _acquired;

    private MutationServicingLock(Mutex? mutex, bool acquired)
    {
        _mutex = mutex;
        _acquired = acquired;
    }

    public static MutationServicingLock TryAcquire()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MutationServicingLock(null, true);
        }

        var mutex = new Mutex(initiallyOwned: false, MutexName, out _);
        var acquired = mutex.WaitOne(TimeSpan.Zero);
        return new MutationServicingLock(mutex, acquired);
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
