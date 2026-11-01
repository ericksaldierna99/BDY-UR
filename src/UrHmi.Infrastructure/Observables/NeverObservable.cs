namespace UrHmi.Infrastructure.Observables;

internal sealed class NeverObservable<T> : IObservable<T>
{
    public static readonly NeverObservable<T> Instance = new();

    private NeverObservable()
    {
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
