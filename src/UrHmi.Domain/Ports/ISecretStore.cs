namespace UrHmi.Domain.Ports;

/// <summary>
/// Secrets must not be stored in the unprotected JSON configuration file.
/// The concrete mechanism (Credential Manager, DPAPI, or corporate store) is still open.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);
}
