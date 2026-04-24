using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskScout.ViewModels;

public sealed partial class DriveSelectionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string RootPath { get; }
    public string DisplayText { get; }

    public DriveSelectionItemViewModel(string rootPath, string displayText, bool selectedByDefault)
    {
        RootPath = rootPath;
        DisplayText = displayText;
        _isSelected = selectedByDefault;
    }
}
