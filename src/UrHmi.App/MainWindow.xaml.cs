using System.Windows;
using UrHmi.Presentation.Shell;

namespace UrHmi.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
