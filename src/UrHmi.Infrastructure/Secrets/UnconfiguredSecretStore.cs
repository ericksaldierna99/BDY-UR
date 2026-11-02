using UrHmi.Domain.Ports;

namespace UrHmi.Infrastructure.Secrets;

public sealed class UnconfiguredSecretStore : ISecretStore
{
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult<string?>(null);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        throw new InvalidOperationException(
            "Secret store mechanism is not configured. Secrets must not be written to JSON configuration.");
    }
}
