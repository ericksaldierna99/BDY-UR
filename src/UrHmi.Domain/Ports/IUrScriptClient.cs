namespace UrHmi.Domain.Ports;

/// <summary>
/// Secondary/primary URScript channel (TCP 30002, fallback 30001). Short-lived send of a script.
/// </summary>
public interface IUrScriptClient
{
    Task SendAsync(string script, CancellationToken cancellationToken = default);
}
