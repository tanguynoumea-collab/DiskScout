using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskScout.ViewModels;

public sealed partial class ProgramsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir les programmes installés.";

    [ObservableProperty]
    private bool _hasResults;
}
