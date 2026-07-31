using Microsoft.Win32;

namespace Bridge.Services;

public interface IFolderPickerService
{
    /// <summary>Returns the chosen folder path, or null if the user cancelled.</summary>
    string? PickFolder(string title);
}

public class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
