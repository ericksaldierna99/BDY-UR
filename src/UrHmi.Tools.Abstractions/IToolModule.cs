namespace UrHmi.Tools.Abstractions;

public interface IToolModule
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<string> RequiredPlcTags { get; }

    object CreateViewModel(IToolHost host);
}
