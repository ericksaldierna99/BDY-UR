namespace UrHmi.Tools.Abstractions;

public interface IToolViewFactory
{
    Type ViewModelType { get; }

    Type ViewType { get; }

    object CreateView(object viewModel);
}
