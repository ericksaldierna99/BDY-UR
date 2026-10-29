using System.Windows.Threading;
using UrHmi.Presentation.Shell;

namespace UrHmi.App.Dispatching;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}
