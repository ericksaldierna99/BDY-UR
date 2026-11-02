using UrHmi.Tools.Abstractions;

namespace UrHmi.Infrastructure.Plugins;

public sealed record PluginLoadFailure(string AssemblyPath, string Message);

public sealed record PluginLoadResult(
    IReadOnlyList<IToolModule> Modules,
    IReadOnlyList<IToolViewFactory> Views,
    IReadOnlyList<PluginLoadFailure> Failures);
