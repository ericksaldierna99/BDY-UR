using UrHmi.Domain.Alarms;
using UrHmi.Domain.Ports;

namespace UrHmi.Infrastructure.Alarms;

public sealed class InMemoryAlarmStore : IAlarmStore, IAlarmSink
{
    private readonly object _gate = new();
    private readonly List<AlarmRecord> _records = [];

    public Task RaiseAsync(AlarmRecord alarm, CancellationToken cancellationToken = default)
    {
        Raise(alarm);
        return Task.CompletedTask;
    }

    public Task ClearAsync(string alarmId, DateTimeOffset clearedUtc, CancellationToken cancellationToken = default)
    {
        Clear(alarmId, clearedUtc);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            IEnumerable<AlarmRecord> items = _records;
            if (query.Origin is { } origin)
            {
                items = items.Where(r => r.Origin == origin);
            }

            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                items = items.Where(r => string.Equals(r.Category, query.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (query.FromUtc is { } from)
            {
                items = items.Where(r => r.RaisedUtc >= from);
            }

            if (query.ToUtc is { } to)
            {
                items = items.Where(r => r.RaisedUtc <= to);
            }

            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                items = items.Where(r => r.Description.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
            }

            if (query.IsActive is { } active)
            {
                items = items.Where(r => r.IsActive == active);
            }

            return Task.FromResult<IReadOnlyList<AlarmRecord>>(items.ToArray());
        }
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_records.Count(r => r.IsActive));
        }
    }

    public void Raise(AlarmRecord alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        lock (_gate)
        {
            _records.RemoveAll(r => r.Id == alarm.Id);
            _records.Add(alarm);
        }
    }

    public void Clear(string alarmId, DateTimeOffset clearedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmId);
        lock (_gate)
        {
            var index = _records.FindIndex(r => r.Id == alarmId);
            if (index < 0)
            {
                return;
            }

            var current = _records[index];
            _records[index] = current with
            {
                ClearedUtc = clearedUtc,
                IsActive = false,
                Duration = clearedUtc - current.RaisedUtc
            };
        }
    }
}
