using CommunityToolkit.Mvvm.ComponentModel;

namespace UrHmi.Presentation.Jems;

public sealed class JemsViewModel : ObservableObject
{
    public string Title => "JEMS";

    public string EmptyMessage =>
        "JEMS is a placeholder module. It will be developed later.";
}
