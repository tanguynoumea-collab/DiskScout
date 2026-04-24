using System.Windows;

namespace DiskScout.Helpers;

public static class DeletePrompt
{
    /// <summary>
    /// Returns (confirmed, permanent). Confirmed=false means the user cancelled.
    /// permanent=true means skip the Recycle Bin.
    /// </summary>
    public static (bool Confirmed, bool Permanent) Ask(string summary)
    {
        var body =
            summary + Environment.NewLine + Environment.NewLine +
            "Par défaut : envoyé à la corbeille (réversible)." + Environment.NewLine +
            "« Oui »  : corbeille (recommandé)" + Environment.NewLine +
            "« Non »  : suppression DÉFINITIVE (irréversible)" + Environment.NewLine +
            "« Annuler » : ne rien faire";

        var result = MessageBox.Show(
            body,
            "DiskScout — supprimer ?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        return result switch
        {
            MessageBoxResult.Yes => (true, false),
            MessageBoxResult.No => ConfirmPermanent(summary),
            _ => (false, false),
        };
    }

    private static (bool Confirmed, bool Permanent) ConfirmPermanent(string summary)
    {
        var result = MessageBox.Show(
            "SUPPRESSION DÉFINITIVE demandée." + Environment.NewLine + Environment.NewLine +
            summary + Environment.NewLine + Environment.NewLine +
            "Cette action est IRRÉVERSIBLE. Confirmer ?",
            "DiskScout — confirmer la suppression définitive",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Stop,
            MessageBoxResult.Cancel);

        return (result == MessageBoxResult.OK, true);
    }

    public static void ShowResult(DiskScout.Services.DeletionResult result)
    {
        var lines = new List<string>
        {
            $"{result.SuccessCount} supprimé(s), {result.FailureCount} échec(s).",
            $"Espace libéré : {FormatBytes(result.TotalBytesFreed)}.",
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

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
    }
}
