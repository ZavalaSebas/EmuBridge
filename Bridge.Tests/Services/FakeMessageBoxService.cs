using System.Windows;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeMessageBoxService : IMessageBoxService
{
    public bool ShowCalled { get; private set; }
    public int ShowCallCount { get; private set; }
    public string? LastMessage { get; private set; }
    public string? LastCaption { get; private set; }
    public MessageBoxResult NextResult { get; set; } = MessageBoxResult.OK;

    public MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        ShowCalled = true;
        ShowCallCount++;
        LastMessage = message;
        LastCaption = caption;
        return NextResult;
    }
}
