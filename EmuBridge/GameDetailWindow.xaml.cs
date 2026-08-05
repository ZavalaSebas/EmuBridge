using System.Windows;
using EmuBridge.ViewModels;

namespace EmuBridge;

public partial class GameDetailWindow : Window
{
    public GameDetailWindow()
    {
        InitializeComponent();
        Loaded += GameDetailWindow_Loaded;
    }

    private async void GameDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameDetailViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
