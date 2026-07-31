using Microsoft.Win32;

namespace Bridge.Services;

public interface IFilePickerService
{
    /// <summary>Returns the chosen file path, or null if the user cancelled.</summary>
    string? PickFile(string title, string filter);
}

public class FilePickerService : IFilePickerService
{
    public string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
