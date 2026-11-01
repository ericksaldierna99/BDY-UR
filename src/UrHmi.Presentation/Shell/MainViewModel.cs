using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UrHmi.Domain.Configuration;
using UrHmi.Presentation.Navigation;

namespace UrHmi.Presentation.Shell;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(IEnumerable<IWorkspaceModule> modules, HmiConfiguration configuration)
    {
        Modules = new ObservableCollection<IWorkspaceModule>(
            modules.OrderBy(module => module.SortOrder));

        var initialId = configuration.SafeMode ? "settings" : "dashboard";
        CurrentModule = Modules.FirstOrDefault(module => module.Id == initialId) ?? Modules.FirstOrDefault();
        CurrentViewModel = CurrentModule?.CreateViewModel();
    }

    public ObservableCollection<IWorkspaceModule> Modules { get; }

    [ObservableProperty]
    private IWorkspaceModule? _currentModule;

    [ObservableProperty]
    private object? _currentViewModel;

    [RelayCommand]
    private void Navigate(IWorkspaceModule? module)
    {
        if (module is null)
        {
            return;
        }

        CurrentModule = module;
    }

    partial void OnCurrentViewModelChanging(object? value)
    {
        if (_currentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    partial void OnCurrentModuleChanged(IWorkspaceModule? value)
    {
        CurrentViewModel = value?.CreateViewModel();
    }
}
