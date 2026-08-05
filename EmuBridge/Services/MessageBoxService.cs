using System.Windows;

namespace EmuBridge.Services;

public interface IMessageBoxService
{
    MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage icon);
}

public class MessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return MessageBox.Show(message, caption, button, icon);
    }
}
