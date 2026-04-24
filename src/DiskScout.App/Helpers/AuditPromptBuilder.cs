using System.Text;
using System.Windows;

namespace DiskScout.Helpers;

public sealed record AuditItem(string FullPath, long SizeBytes, string? Reason = null, string? Extra = null);

public static class AuditPromptBuilder
{
    /// <summary>
    /// Build a self-contained audit prompt and copy to clipboard.
    /// Returns true if copied, false if nothing to audit.
    /// </summary>
    public static bool BuildAndCopy(string tabContext, IReadOnlyList<AuditItem> items)
    {
        if (items.Count == 0)
        {
            MessageBox.Show(
                "Aucun élément sélectionné. Coche d'abord les entrées dont tu veux vérifier la dangerosité de suppression.",
                "DiskScout — Log IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var prompt = Build(tabContext, items);
        try { Clipboard.SetText(prompt); }
        catch
        {
            MessageBox.Show("Impossible d'écrire dans le presse-papier. Ré-essaie.",
                "DiskScout", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        MessageBox.Show(
            $"Prompt pour {items.Count} élément(s) copié dans le presse-papier ({prompt.Length:n0} caractères).\n\n" +
            "Colle-le dans ChatGPT, Claude, Claude Cowork, Gemini, Mistral, etc. " +
            "L'IA analysera chaque chemin et te dira pour chacun s'il est sûr de supprimer.",
            "DiskScout — Log IA",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return true;
    }

    public static string Build(string tabContext, IReadOnlyList<AuditItem> items)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Tu es expert en administration système Windows et en sécurité des données.");
        sb.AppendLine("J'utilise DiskScout (outil d'analyse disque) pour nettoyer mon disque dur.");
        sb.AppendLine();
        sb.AppendLine($"Contexte : onglet **{tabContext}** de DiskScout.");
        sb.AppendLine();
        sb.AppendLine("Pour chaque élément ci-dessous, analyse la dangerosité de le supprimer et réponds en suivant STRICTEMENT ce format pour chaque point :");
        sb.AppendLine();
        sb.AppendLine("  [N] {nom de fichier}");
        sb.AppendLine("      🔴 / 🟠 / 🟡 / 🟢 — {Niveau : CRITIQUE / ÉLEVÉ / MOYEN / FAIBLE / AUCUN}");
        sb.AppendLine("      Raison : {1-2 phrases expliquant ce que fait ce fichier/dossier et pourquoi}");
        sb.AppendLine("      Action recommandée : {SUPPRIMER / CORBEILLE OK / GARDER / NE PAS TOUCHER}");
        sb.AppendLine();
        sb.AppendLine("Critères de dangerosité :");
        sb.AppendLine("- 🟢 AUCUN : caches régénérables, Temp > 30j, rémanents désinstallés, doublons certains.");
        sb.AppendLine("- 🟡 FAIBLE : contenu utilisateur non critique mais utile (downloads anciens, logs).");
        sb.AppendLine("- 🟠 MOYEN : fichier utilisateur possiblement encore utilisé (document, projet).");
        sb.AppendLine("- 🔴 ÉLEVÉ : fichier système, configuration, credentials, données actives.");
        sb.AppendLine("- ⛔ CRITIQUE : binaire OS, driver, contenu chiffré, état d'application en cours.");
        sb.AppendLine();
        sb.AppendLine("Termine par une **synthèse finale** listant : (a) éléments à supprimer sans risque, (b) à vérifier avant suppression, (c) à ABSOLUMENT garder.");
        sb.AppendLine();
        sb.AppendLine("==========================================");
        sb.AppendLine("ÉLÉMENTS À ANALYSER :");
        sb.AppendLine("==========================================");
        sb.AppendLine();

        int idx = 1;
        foreach (var it in items)
        {
            sb.Append('[').Append(idx++).Append("] ").AppendLine(it.FullPath);
            sb.Append("    Taille : ").AppendLine(FormatBytes(it.SizeBytes));
            if (!string.IsNullOrWhiteSpace(it.Reason))
            {
                sb.Append("    Détection DiskScout : ").AppendLine(it.Reason);
            }
            if (!string.IsNullOrWhiteSpace(it.Extra))
            {
                sb.Append("    Info : ").AppendLine(it.Extra);
            }
            sb.AppendLine();
        }

        sb.AppendLine("Réponds en français, structure par point numéroté comme demandé.");
        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
    }
}
