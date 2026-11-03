namespace UrHmi.Tools.Screwdriver;

public sealed class NullSpinBridgeClient : ISpinBridgeClient
{
    public static NullSpinBridgeClient Instance { get; } = new();

    public SpinBridgeReading Current => SpinBridgeReading.Unavailable();

    public void Start()
    {
    }

    public void Dispose()
    {
    }

    public IDisposable Subscribe(IObserver<SpinBridgeReading> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        observer.OnNext(Current);
        return Empty.Instance;
    }

    private sealed class Empty : IDisposable
    {
        public static readonly Empty Instance = new();

        public void Dispose()
        {
        }
    }
}
