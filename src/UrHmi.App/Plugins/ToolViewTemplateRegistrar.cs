using System.Windows;
using UrHmi.Tools.Abstractions;

namespace UrHmi.App.Plugins;

internal static class ToolViewTemplateRegistrar
{
    public static void Register(IEnumerable<IToolViewFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        foreach (var factory in factories)
        {
            Register(application.Resources, factory);
        }
    }

    internal static void Register(ResourceDictionary resources, IToolViewFactory factory)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(factory);
        var key = new DataTemplateKey(factory.ViewModelType);
        if (resources.Contains(key))
        {
            return;
        }

        var visual = new FrameworkElementFactory(factory.ViewType);
        var template = new DataTemplate(factory.ViewModelType)
        {
            VisualTree = visual,
            DataType = factory.ViewModelType
        };
        resources.Add(key, template);
    }
}
