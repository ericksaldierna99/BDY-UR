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
            var key = new DataTemplateKey(factory.ViewModelType);
            if (application.Resources.Contains(key))
            {
                continue;
            }

            var visual = new FrameworkElementFactory(factory.ViewType);
            var template = new DataTemplate(factory.ViewModelType) { VisualTree = visual };
            application.Resources.Add(key, template);
        }
    }
}
