using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeFilePickerService : IFilePickerService
{
    public string? NextResult { get; set; }
    public bool PickFileCalled { get; private set; }

    public string? PickFile(string title, string filter)
    {
        PickFileCalled = true;
        return NextResult;
    }
}
