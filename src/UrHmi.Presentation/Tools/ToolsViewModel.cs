using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UrHmi.Application.Authorization;
using UrHmi.Application.Observables;
using UrHmi.Application.Telemetry;
using UrHmi.Application.Tools;
using UrHmi.Domain;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Presentation.Shell;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Presentation.Tools;

public sealed partial class ToolsViewModel : ObservableObject, IDisposable
{
    private readonly IToolCatalog _catalog;
    private readonly IToolHost _host;
    private readonly IConfigStore _configStore;
    private readonly IAuthorizationService _authorization;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDisposable _plcSubscription;
    private int _disposed;

    public ToolsViewModel(
        IToolCatalog catalog,
        IToolHost host,
        IPlcTelemetry plc,
        IConfigStore configStore,
        IAuthorizationService authorization,
        IUiDispatcher dispatcher)
    {
        _catalog = catalog;
        _host = host;
        _configStore = configStore;
        _authorization = authorization;
        _dispatcher = dispatcher;

        Modules = new ObservableCollection<ToolListItem>(
            catalog.Enabled.Select(module => new ToolListItem(module.Id, module.DisplayName, module)));

        ApplyPlc(plc.Current);
        _plcSubscription = plc.Snapshots.Subscribe(new DelegateObserver<PlcSnapshot>(snapshot =>
            OnUi(() => ApplyPlc(snapshot))));

        if (catalog.Selected is { } selected)
        {
            SelectedModule = Modules.FirstOrDefault(item =>
                string.Equals(item.Id, selected.Id, StringComparison.Ordinal));
        }

        RefreshEmptyMessage();
    }

    public string Title => "Tool Management";

    public ObservableCollection<ToolListItem> Modules { get; }

    [ObservableProperty]
    private ToolListItem? _selectedModule;

    [ObservableProperty]
    private object? _activeToolViewModel;

    [ObservableProperty]
    private string _frameName = "No tool selected";

    [ObservableProperty]
    private string _plcLinkText = LinkState.Disconnected.ToString();

    [ObservableProperty]
    private string _enablementText = "Disabled";

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    private string? _selectionError;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _plcSubscription.Dispose();
        DisposeActiveViewModel();
    }

    partial void OnSelectedModuleChanged(ToolListItem? value)
    {
        DisposeActiveViewModel();
        SelectionError = null;

        if (value is null)
        {
            FrameName = "No tool selected";
            EnablementText = "Disabled";
            ActiveToolViewModel = null;
            RefreshEmptyMessage();
            return;
        }

        try
        {
            ActiveToolViewModel = value.Module.CreateViewModel(_host);
            FrameName = value.DisplayName;
            EnablementText = "Enabled";
            PersistSelection(value.Id);
        }
        catch (Exception ex)
        {
            SelectionError = ex.Message;
            FrameName = value.DisplayName;
            EnablementText = "Faulted";
            ActiveToolViewModel = null;
        }

        RefreshEmptyMessage();
    }

    private void PersistSelection(string id)
    {
        if (!_authorization.HasProcessChangePermission())
        {
            return;
        }

        _ = PersistSelectionAsync(id);
    }

    private async Task PersistSelectionAsync(string id)
    {
        try
        {
            var current = await _configStore.LoadAsync().ConfigureAwait(false);
            if (string.Equals(current.Tools.SelectedId, id, StringComparison.Ordinal))
            {
                return;
            }

            var updated = current with
            {
                Tools = current.Tools with { SelectedId = id }
            };
            await _configStore.SaveAsync(updated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnUi(() => SelectionError = ex.Message);
        }
    }

    private void ApplyPlc(PlcSnapshot snapshot)
    {
        PlcLinkText = snapshot.Channel.Link.ToString();
    }

    private void RefreshEmptyMessage()
    {
        if (ActiveToolViewModel is not null)
        {
            EmptyMessage = string.Empty;
            return;
        }

        if (_catalog.Discovered.Count == 0)
        {
            EmptyMessage =
                "No tool module is loaded. Core does not reference concrete tooling assemblies; plugins are loaded from the plugins directory.";
            return;
        }

        if (Modules.Count == 0)
        {
            EmptyMessage = "No tool module is enabled. Set tools.enabled in configuration to activate a loaded plugin.";
            return;
        }

        EmptyMessage = "Select a tool module.";
    }

    private void DisposeActiveViewModel()
    {
        if (ActiveToolViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }
}

public sealed record ToolListItem(string Id, string DisplayName, IToolModule Module);
