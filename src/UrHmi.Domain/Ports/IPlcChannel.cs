using UrHmi.Domain.Plc;

namespace UrHmi.Domain.Ports;

/// <summary>
/// Sole PLC boundary. EtherNet/IP and Modbus TCP are future implementations of this port.
/// </summary>
public interface IPlcChannel : IAsyncDisposable
{
    ChannelHealth Health { get; }

    IObservable<PlcSnapshot> Snapshots { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<WriteResult> WriteAsync(PlcWriteRequest request, CancellationToken cancellationToken = default);
}
