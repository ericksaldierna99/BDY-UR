using CommunityToolkit.Mvvm.ComponentModel;

namespace UrHmi.Presentation.Dashboard;

public sealed partial class JogSlotItem : ObservableObject
{
    public JogSlotItem(int slot)
    {
        Slot = slot;
    }

    public int Slot { get; }

    public string Title => $"Position {Slot}";

    [ObservableProperty]
    private string _caption = "empty";
}
