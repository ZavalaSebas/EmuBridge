using System.Windows;
using System.Windows.Media.Animation;
using EmuBridge.ViewModels;
using Wpf.Ui.Controls;

namespace EmuBridge;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    // The WindowState binding is deliberately Mode=OneWay (the converter's ConvertBack throws
    // NotSupportedException — a manual window-state change, e.g. the titlebar maximize button or
    // Win+Up, used to push back through it and crash the app). The mode<->window-state round-trip
    // is kept here instead, preserving ADR-22's "maximizes on activation" in both directions:
    // maximizing engages Big Picture, restoring exits it, and minimizing never disturbs the mode.
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized || DataContext is not MainViewModel viewModel)
        {
            return;
        }
        viewModel.IsBigPictureMode = WindowState == WindowState.Maximized;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }

        // Phase Polish -> "Polished transition animations": a short fade-in so the window doesn't
        // pop into existence. Opacity is animated (not set in XAML) so a failed animation can
        // never leave the window invisible.
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }
}
