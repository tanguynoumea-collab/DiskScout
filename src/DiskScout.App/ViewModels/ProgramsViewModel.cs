using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class ProgramsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir les programmes installés.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _count;

    /// <summary>Currently selected row in the Programs DataGrid (drives UninstallSelectedCommand).</summary>
    [ObservableProperty]
    private InstalledProgramRow? _selectedRow;

    public ObservableCollection<InstalledProgramRow> Rows { get; } = new();

    public ICollectionView View { get; }

    /// <summary>
    /// Raised when the user invokes Uninstall on a selected row. ProgramsTabView listens to this
    /// and opens the Uninstall Wizard window via App.OpenUninstallWizard.
    /// </summary>
    public event EventHandler<InstalledProgram>? UninstallRequested;

    public ProgramsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Rows);
        View.SortDescriptions.Add(new SortDescription(nameof(InstalledProgramRow.SizeBytes), ListSortDirection.Descending));
        View.Filter = FilterPredicate;
    }

    partial void OnSearchTextChanged(string value) => View.Refresh();

    partial void OnSelectedRowChanged(InstalledProgramRow? value)
    {
        UninstallSelectedCommand.NotifyCanExecuteChanged();
    }

    public void Load(IEnumerable<InstalledProgram> programs)
    {
        Rows.Clear();
        foreach (var p in programs)
        {
            Rows.Add(new InstalledProgramRow(p));
        }
        Count = Rows.Count;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

    /// <summary>
    /// Re-creates rows preserving the same source InstalledProgram with augmented metadata.
    /// Plan 09-06: MainViewModel.OnScanCompleted calls this with the trace/rule dictionaries
    /// derived from <c>IInstallTraceStore.ListAsync()</c> + <c>IPublisherRuleEngine.Match(...)</c>.
    /// </summary>
    public void Annotate(IDictionary<string, bool> tracedByRegistryKey, IDictionary<string, string> rulesByDisplayName)
    {
        if (tracedByRegistryKey is null) throw new ArgumentNullException(nameof(tracedByRegistryKey));
        if (rulesByDisplayName is null) throw new ArgumentNullException(nameof(rulesByDisplayName));

        var snapshot = Rows.Select(r => r.Source).ToList();
        Rows.Clear();
        foreach (var p in snapshot)
        {
            tracedByRegistryKey.TryGetValue(p.RegistryKey, out var traced);
            rulesByDisplayName.TryGetValue(p.DisplayName, out var ruleIds);
            Rows.Add(new InstalledProgramRow(p, hasInstallTrace: traced, matchedPublisherRuleIds: ruleIds ?? ""));
        }
        Count = Rows.Count;
        View.Refresh();
    }

    private bool FilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (obj is not InstalledProgramRow r) return false;
        return (r.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.Publisher?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private void UninstallSelected()
    {
        if (SelectedRow is null) return;
        UninstallRequested?.Invoke(this, SelectedRow.Source);
    }

    private bool CanUninstall() => SelectedRow is not null;
}

public sealed class InstalledProgramRow
{
    public InstalledProgramRow(InstalledProgram p, bool hasInstallTrace = false, string matchedPublisherRuleIds = "")
    {
        Source = p;
        DisplayName = p.DisplayName;
        Publisher = p.Publisher;
        Version = p.Version;
        InstallDate = p.InstallDate?.ToString("yyyy-MM-dd");
        InstallLocation = p.InstallLocation;
        SizeBytes = p.ComputedSizeBytes > 0 ? p.ComputedSizeBytes : p.RegistryEstimatedSizeBytes;
        Hive = p.Hive.ToString();
        HasInstallTrace = hasInstallTrace;
        MatchedPublisherRuleIds = matchedPublisherRuleIds ?? string.Empty;
    }

    /// <summary>The original InstalledProgram for re-annotation + Wizard launch.</summary>
    internal InstalledProgram Source { get; }

    public string DisplayName { get; }
    public string? Publisher { get; }
    public string? Version { get; }
    public string? InstallDate { get; }
    public string? InstallLocation { get; }
    public long SizeBytes { get; }
    public string SizeDisplay => FormatBytes(SizeBytes);
    public string Hive { get; }

    /// <summary>True when an install trace exists for this program (Plan 09-01).</summary>
    public bool HasInstallTrace { get; }

    /// <summary>Comma-separated matched publisher-rule ids (Plan 09-04). Empty when no rule matches.</summary>
    public string MatchedPublisherRuleIds { get; }

    /// <summary>Display column for the "Tracé ?" DataGrid column.</summary>
    public string TracedDisplay => HasInstallTrace ? "✓" : "";

    /// <summary>Display column for the "Règles éditeur" DataGrid column.</summary>
    public string RuleDisplay => MatchedPublisherRuleIds;

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int unit = 0;
        while (v >= 1024 && unit < units.Length - 1) { v /= 1024; unit++; }
        return $"{v:F1} {units[unit]}";
    }
}
