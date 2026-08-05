using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeFolderPickerService : IFolderPickerService
{
    public string? NextResult { get; set; }
    public bool PickFolderCalled { get; private set; }

    public string? PickFolder(string title)
    {
        PickFolderCalled = true;
        return NextResult;
    }
}
