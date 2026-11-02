using System.Windows;
using UrHmi.Presentation.Shell;

namespace UrHmi.App.Dispatching;

public sealed class WpfUserPrompt : IUserPrompt
{
    private readonly IUiDispatcher _dispatcher;

    public WpfUserPrompt(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool Confirm(string title, string message) =>
        _dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

    public void Inform(string title, string message) =>
        _dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information));
}
