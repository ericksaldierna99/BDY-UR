using UrHmi.Domain.Configuration;

namespace UrHmi.Domain.Ports;

public interface IConfigStore
{
    Task<HmiConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(HmiConfiguration configuration, CancellationToken cancellationToken = default);
}
