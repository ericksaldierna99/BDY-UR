using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Infrastructure.Plugins;

public sealed class PluginDirectoryLoader
{
    private readonly ILogger<PluginDirectoryLoader> _logger;

    public PluginDirectoryLoader(ILogger<PluginDirectoryLoader> logger)
    {
        _logger = logger;
    }

    public PluginLoadResult Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            _logger.LogInformation("Tool plugin directory {Directory} does not exist.", directory);
            return new PluginLoadResult([], [], []);
        }

        var modules = new List<IToolModule>();
        var views = new List<IToolViewFactory>();
        var failures = new List<PluginLoadFailure>();

        foreach (var path in Directory.GetFiles(directory, "UrHmi.Tools.*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, "UrHmi.Tools.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var assembly = LoadAssembly(path);
                var exportedModules = 0;
                var exportedViews = 0;
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.IsAbstract)
                    {
                        continue;
                    }

                    if (typeof(IToolModule).IsAssignableFrom(type) &&
                        Activator.CreateInstance(type) is IToolModule module)
                    {
                        modules.Add(module);
                        exportedModules++;
                        if (module is IToolViewFactory dual)
                        {
                            views.Add(dual);
                            exportedViews++;
                        }

                        continue;
                    }

                    if (typeof(IToolViewFactory).IsAssignableFrom(type) &&
                        Activator.CreateInstance(type) is IToolViewFactory factory)
                    {
                        views.Add(factory);
                        exportedViews++;
                    }
                }

                _logger.LogInformation(
                    "Loaded tool plugin {Path} ({ModuleCount} module(s), {ViewCount} view factory(ies)).",
                    path,
                    exportedModules,
                    exportedViews);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tool plugin {Path}.", path);
                failures.Add(new PluginLoadFailure(path, ex.Message));
            }
        }

        return new PluginLoadResult(modules, views, failures);
    }

    private static Assembly LoadAssembly(string path)
    {
        var context = new PluginLoadContext(path);
        return context.LoadFromAssemblyPath(Path.GetFullPath(path));
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (var loaded in Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.Ordinal))
                {
                    return loaded;
                }
            }

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolved is null ? null : LoadFromAssemblyPath(resolved);
        }
    }
}
