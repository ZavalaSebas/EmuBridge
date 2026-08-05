using System.Windows;
using EmuBridge.ViewModels;

namespace EmuBridge;

public partial class CheatsWindow : Window
{
    public CheatsWindow()
    {
        InitializeComponent();
        Loaded += CheatsWindow_Loaded;
    }

    private async void CheatsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CheatsViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
