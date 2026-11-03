using UrHmi.Tools.Abstractions;

namespace UrHmi.Application.Tools;

public interface IToolCatalog
{
    IReadOnlyList<IToolModule> Discovered { get; }

    IReadOnlyList<IToolModule> Enabled { get; }

    IToolModule? Selected { get; }

    IReadOnlyList<IToolViewFactory> Views { get; }

    IToolModule? FindEnabled(string id);
}
