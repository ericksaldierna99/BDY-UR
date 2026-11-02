namespace UrHmi.Application.Observables;

public sealed class SnapshotBroadcaster<T> : IObservable<T>
{
    private readonly object _gate = new();
    private readonly List<IObserver<T>> _observers = [];
    private T _current;

    public SnapshotBroadcaster(T initial)
    {
        _current = initial;
    }

    public T Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Publish(T value)
    {
        IObserver<T>[] observers;
        lock (_gate)
        {
            _current = value;
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            observer.OnNext(value);
        }
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        T current;
        lock (_gate)
        {
            _observers.Add(observer);
            current = _current;
        }

        observer.OnNext(current);
        return new Subscription(this, observer);
    }

    private void Unsubscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private SnapshotBroadcaster<T>? _owner;
        private readonly IObserver<T> _observer;

        public Subscription(SnapshotBroadcaster<T> owner, IObserver<T> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unsubscribe(_observer);
        }
    }
}
