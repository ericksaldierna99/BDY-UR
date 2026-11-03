using UrHmi.Tools.Abstractions;
using UrHmi.Tools.Screwdriver.Ui.Views;

namespace UrHmi.Tools.Screwdriver.Ui;

public sealed class ScrewdriverViewFactory : IToolViewFactory
{
    public Type ViewModelType => typeof(ScrewdriverViewModel);

    public Type ViewType => typeof(ScrewdriverView);

    public object CreateView(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return new ScrewdriverView { DataContext = viewModel };
    }
}
