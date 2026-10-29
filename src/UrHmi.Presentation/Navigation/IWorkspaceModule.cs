namespace UrHmi.Presentation.Navigation;

/// <summary>
/// Shell extension point. Specified modules register here. Spec section 5.4 is unspecified
/// and must not get a fake screen; future modules implement this contract when a spec exists.
/// </summary>
public interface IWorkspaceModule
{
    string Id { get; }

    string DisplayName { get; }

    int SortOrder { get; }

    object CreateViewModel();
}
