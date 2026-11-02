namespace UrHmi.Presentation.Navigation;

public sealed class WorkspaceModule : IWorkspaceModule
{
    private readonly Func<object> _factory;

    public WorkspaceModule(string id, string displayName, int sortOrder, Func<object> factory)
    {
        Id = id;
        DisplayName = displayName;
        SortOrder = sortOrder;
        _factory = factory;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int SortOrder { get; }

    public object CreateViewModel() => _factory();
}
