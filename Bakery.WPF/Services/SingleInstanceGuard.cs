using System.Threading;

namespace Bakery.WPF.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string ProductionMutexName = "BakeryERP.A84F2B76-3D1E-4C90-9E11-2B47890123AB";

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public SingleInstanceGuard(string mutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
            throw new ArgumentException("Mutex name is required.", nameof(mutexName));

        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        _ownsMutex = createdNew;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }
}
