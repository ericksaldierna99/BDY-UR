using CommunityToolkit.Mvvm.ComponentModel;

namespace UrHmi.Presentation.Tools;

public sealed class ToolsViewModel : ObservableObject
{
    public string Title => "Tool Management";

    public string EmptyMessage =>
        "No tool module is loaded. Core does not reference concrete tooling assemblies. The plugin shell is a later phase.";
}
