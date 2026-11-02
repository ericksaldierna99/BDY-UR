using UrHmi.Tools.Abstractions;

namespace UrHmi.Tools.Screwdriver;

public sealed class ScrewdriverToolModule : IToolModule
{
    public string Id => "screwdriver";

    public string DisplayName => "Atornillado";

    public IReadOnlyList<string> RequiredPlcTags => ScrewdriverTags.Required;

    public object CreateViewModel(IToolHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new ScrewdriverViewModel(host, ScrewdriverConfigStore.LoadOrCreate());
    }
}
