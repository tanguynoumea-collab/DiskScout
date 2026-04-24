using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskScout.ViewModels;

public sealed partial class OrphansViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour détecter les fichiers rémanents.";

    [ObservableProperty]
    private bool _hasResults;
}
