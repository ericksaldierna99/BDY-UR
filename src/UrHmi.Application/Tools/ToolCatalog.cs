using UrHmi.Domain.Configuration;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Application.Tools;

public sealed class ToolCatalog : IToolCatalog
{
    public ToolCatalog(IReadOnlyList<IToolModule> discovered, ToolsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(configuration);

        Discovered = discovered;
        var enabledIds = new HashSet<string>(configuration.Enabled, StringComparer.Ordinal);
        Enabled = discovered
            .Where(module => enabledIds.Contains(module.Id))
            .ToArray();
        Selected = FindEnabled(configuration.SelectedId);
    }

    public IReadOnlyList<IToolModule> Discovered { get; }

    public IReadOnlyList<IToolModule> Enabled { get; }

    public IToolModule? Selected { get; }

    public IToolModule? FindEnabled(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Enabled.FirstOrDefault(module =>
            string.Equals(module.Id, id, StringComparison.Ordinal));
    }
}
