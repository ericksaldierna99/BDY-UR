namespace UrHmi.Presentation.Navigation;

/// <summary>
/// Shell extension point. Specified modules register here. Confirmed placeholders such as JEMS
/// may register an empty view. Spec section 5.4 stays unspecified.
/// </summary>
public interface IWorkspaceModule
{
    string Id { get; }

    string DisplayName { get; }

    int SortOrder { get; }

    object CreateViewModel();
}
