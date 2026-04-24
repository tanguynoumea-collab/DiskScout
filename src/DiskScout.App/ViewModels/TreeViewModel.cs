using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskScout.ViewModels;

public sealed partial class TreeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour explorer ton disque.";

    [ObservableProperty]
    private bool _hasResults;
}
