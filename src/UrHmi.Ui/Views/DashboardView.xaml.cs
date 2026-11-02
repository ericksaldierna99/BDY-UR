using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UrHmi.Presentation.Dashboard;

namespace UrHmi.Ui.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnDeleteHoldDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || DataContext is not DashboardViewModel vm)
        {
            return;
        }

        if (!vm.BeginDeleteHoldCommand.CanExecute(null))
        {
            return;
        }

        ((UIElement)sender).CaptureMouse();
        vm.BeginDeleteHoldCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDeleteHoldUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        ReleaseDeleteHold(sender);
        e.Handled = true;
    }

    private void OnDeleteHoldLost(object sender, MouseEventArgs e) => ReleaseDeleteHold(sender);

    private void ReleaseDeleteHold(object sender)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        if (DataContext is DashboardViewModel vm)
        {
            vm.CancelDeleteHoldCommand.Execute(null);
        }
    }
}
