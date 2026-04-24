using System.Windows;

namespace DiskScout.Helpers;

public enum DeleteMode
{
    Cancelled,
    DiskScoutQuarantine,
    RecycleBin,
    Permanent,
}

public static class DeletePrompt
{
    /// <summary>
    /// Shows a 3-way dialog: Quarantaine DiskScout (30j, managed) / Corbeille Windows / Définitif.
    /// Returns the chosen mode. Permanent requires a second confirmation.
    /// </summary>
    public static DeleteMode Ask(string summary)
    {
        var body =
            summary + Environment.NewLine + Environment.NewLine +
            "Trois choix pour la suppression :" + Environment.NewLine + Environment.NewLine +
            "• « Oui »     → QUARANTAINE DiskScout (rétention 30 jours, restaurable via l'onglet Quarantaine) — RECOMMANDÉ" + Environment.NewLine +
            "• « Non »     → Corbeille Windows (réversible tant que vidée)" + Environment.NewLine +
            "• « Annuler » → ne rien faire" + Environment.NewLine + Environment.NewLine +
            "Pour une suppression DÉFINITIVE (irréversible), maintiens Shift enfoncé en cliquant Non.";

        var result = MessageBox.Show(
            body,
            "DiskScout — supprimer ?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        return result switch
        {
            MessageBoxResult.Yes => DeleteMode.DiskScoutQuarantine,
            MessageBoxResult.No => (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
                ? ConfirmPermanent(summary)
                : DeleteMode.RecycleBin,
            _ => DeleteMode.Cancelled,
        };
    }

    private static DeleteMode ConfirmPermanent(string summary)
    {
        var result = MessageBox.Show(
            "SUPPRESSION DÉFINITIVE demandée (Shift + Non)." + Environment.NewLine + Environment.NewLine +
            summary + Environment.NewLine + Environment.NewLine +
            "Cette action est IRRÉVERSIBLE. Confirmer ?",
            "DiskScout — confirmer la suppression définitive",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Stop,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK ? DeleteMode.Permanent : DeleteMode.Cancelled;
    }

    public static void ShowResult(DiskScout.Services.DeletionResult result, DeleteMode mode)
    {
        var modeLabel = mode switch
        {
            DeleteMode.DiskScoutQuarantine => "quarantaine DiskScout (restaurable 30 j)",
            DeleteMode.RecycleBin => "corbeille Windows",
            DeleteMode.Permanent => "suppression DÉFINITIVE",
            _ => "",
        };

        var lines = new List<string>
        {
            $"{result.SuccessCount} traité(s), {result.FailureCount} échec(s).",
            $"Espace libéré : {ByteFormat.Fmt(result.TotalBytesFreed)}",
            $"Mode : {modeLabel}.",
        };
        if (result.FailureCount > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Échecs :");
            foreach (var e in result.Entries.Where(e => !e.Success).Take(10))
            {
                lines.Add($"  • {e.Path} — {e.Error}");
            }
        }

        MessageBox.Show(
            string.Join(Environment.NewLine, lines),
            "DiskScout — suppression",
            MessageBoxButton.OK,
            result.FailureCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    public static string FormatBytes(long bytes) => ByteFormat.Fmt(bytes);
}
