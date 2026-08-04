using CommunityToolkit.Mvvm.ComponentModel;

namespace Bridge.ViewModels;

// One toggleable row in CheatsWindow. Mutable (unlike GameTile, rebuilt wholesale elsewhere) since
// a checkbox needs a real two-way-bindable property to toggle without rebuilding the whole list on
// every click.
public partial class CheatItem : ObservableObject
{
    public int Index { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _enabled;

    public CheatItem(int index, string description, bool enabled)
    {
        Index = index;
        Description = description;
        _enabled = enabled;
    }
}
