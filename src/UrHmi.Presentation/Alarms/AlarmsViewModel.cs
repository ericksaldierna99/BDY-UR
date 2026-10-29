using CommunityToolkit.Mvvm.ComponentModel;

namespace UrHmi.Presentation.Alarms;

public sealed class AlarmsViewModel : ObservableObject
{
    public string Title => "Alarm Management";

    public string EmptyMessage => "No alarms recorded. Persistence, filters, and CSV export arrive in a later phase.";
}
