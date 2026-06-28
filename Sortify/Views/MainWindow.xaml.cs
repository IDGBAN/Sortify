using System.Windows;
using Sortify.ViewModels;

namespace Sortify.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
