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
        var context = new PluginLoadContext(directory);

        foreach (var path in Directory.GetFiles(directory, "UrHmi.Tools.*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, "UrHmi.Tools.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var assembly = context.LoadPlugin(path);
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

    /// <summary>
    /// One load context for the whole plugins directory so a logic assembly and its
    /// WPF view factory share the same <see cref="Type"/> instances. Separate ALCs
    /// make DataTemplate keys miss and WPF fall back to the ViewModel type name.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;
        private readonly List<AssemblyDependencyResolver> _resolvers = [];

        public PluginLoadContext(string directory)
            : base("UrHmi.Tools", isCollectible: false)
        {
            _directory = directory;
        }

        public Assembly LoadPlugin(string path)
        {
            var full = Path.GetFullPath(path);
            _resolvers.Add(new AssemblyDependencyResolver(full));
            var simpleName = Path.GetFileNameWithoutExtension(full);
            foreach (var loaded in Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            return LoadFromAssemblyPath(full);
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

            foreach (var loaded in Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.Ordinal))
                {
                    return loaded;
                }
            }

            if (!string.IsNullOrEmpty(assemblyName.Name))
            {
                var probe = Path.Combine(_directory, assemblyName.Name + ".dll");
                if (File.Exists(probe))
                {
                    return LoadFromAssemblyPath(Path.GetFullPath(probe));
                }
            }

            foreach (var resolver in _resolvers)
            {
                var resolved = resolver.ResolveAssemblyToPath(assemblyName);
                if (resolved is not null)
                {
                    return LoadFromAssemblyPath(resolved);
                }
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            foreach (var resolver in _resolvers)
            {
                var native = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                if (native is not null)
                {
                    return LoadUnmanagedDllFromPath(native);
                }
            }

            return IntPtr.Zero;
        }
    }
}
