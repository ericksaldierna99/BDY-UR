namespace UrHmi.Domain.Ports;

/// <summary>
/// Secondary/primary URScript channel (TCP 30002, fallback 30001). Short-lived send of a script.
/// </summary>
public interface IUrScriptClient
{
    Task SendAsync(string script, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete program (primary interface preferred). Use for scripts that must keep running, e.g. freedrive.
    /// </summary>
    Task SendProgramAsync(string script, CancellationToken cancellationToken = default) =>
        SendAsync(script, cancellationToken);
}
