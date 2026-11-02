using UrHmi.Domain.Alarms;

namespace UrHmi.Domain.Ports;

public interface IAlarmStore
{
    Task RaiseAsync(AlarmRecord alarm, CancellationToken cancellationToken = default);

    Task ClearAsync(string alarmId, DateTimeOffset clearedUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);
}
