using UrHmi.Domain.Alarms;

namespace UrHmi.Domain.Ports;

public interface IAlarmSink
{
    void Raise(AlarmRecord alarm);

    void Clear(string alarmId, DateTimeOffset clearedUtc);
}
