using System.Reflection;
using System.Windows;

namespace EmuBridge;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?"}";
    }
}
