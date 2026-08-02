using System.Windows;
using Bridge.ViewModels;

namespace Bridge;

public partial class EmulatorOverrideWindow : Window
{
    public EmulatorOverrideWindow()
    {
        InitializeComponent();
        Loaded += EmulatorOverrideWindow_Loaded;
    }

    private async void EmulatorOverrideWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is EmulatorOverrideViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
