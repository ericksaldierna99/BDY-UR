namespace UrHmi.Tools.Screwdriver;

public interface ISpinBridgeClient : IObservable<SpinBridgeReading>, IDisposable
{
    SpinBridgeReading Current { get; }

    void Start();
}
