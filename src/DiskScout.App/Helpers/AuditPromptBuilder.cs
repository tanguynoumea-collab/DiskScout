using System.Text;
using System.Windows;

namespace DiskScout.Helpers;

public sealed record AuditItem(string FullPath, long SizeBytes, string? Reason = null, string? Extra = null);

/// <summary>
/// Structured, LLM-ready context for a DiskScout tab. Gives the AI enough domain info
/// to judge deletion safety per item.
/// </summary>
public sealed record TabContext(
    string TabName,
    string TabPurpose,
    string DetectionMethod,
    string SafetyGuidance,
    string? PerItemExtraLabel = null);

public static class TabContexts
{
    public static readonly TabContext Remnants = new(
        TabName: "Rémanents",
        TabPurpose:
            "L'onglet Rémanents liste les résidus de programmes qui ont été désinstallés. " +
            "Ce sont des fichiers ou dossiers qui n'ont pas été nettoyés par le désinstalleur officiel et qui traînent depuis.",
        DetectionMethod:
            "DiskScout applique 4 heuristiques :\n" +
            "  (1) AppData orphelins : dossiers dans %LocalAppData%, %AppData%, %ProgramData% dont le nom ne matche AUCUN programme installé d'après le registre (fuzzy matching exact/préfixe puis Jaccard sur tokens Publisher+DisplayName, seuil 0.7).\n" +
            "  (2) Program Files vides : sous-dossiers de Program Files / Program Files (x86) < 1 Mo ou contenant uniquement .log/.txt.\n" +
            "  (3) Temp anciens : fichiers dans %TEMP% ou %LocalAppData%\\Temp non modifiés depuis > 30 jours.\n" +
            "  (4) Patches MSI orphelins : fichiers .msp dans C:\\Windows\\Installer non référencés par un programme installé.",
        SafetyGuidance:
            "La majorité des rémanents sont SÛRS à supprimer : l'application qui les a créés est déjà désinstallée. " +
            "ATTENTION cependant aux profils applicatifs dans AppData : si l'utilisateur compte réinstaller le logiciel, " +
            "il risque de perdre préférences / sauvegardes / licences. " +
            "Les patches MSI orphelins peuvent parfois être nécessaires pour des désinstallations futures de l'app parente — marque-les MOYEN si incertain.",
        PerItemExtraLabel: "Heuristique déclenchée");

    public static readonly TabContext Cleanup = new(
        TabName: "Nettoyage",
        TabPurpose:
            "L'onglet Nettoyage regroupe 3 catégories d'espace récupérable facilement : artefacts système Windows, " +
            "caches de développement (langages/frameworks), et caches de navigateurs web.",
        DetectionMethod:
            "Détection par patterns de chemin absolus et noms de dossier connus :\n" +
            "  • Artefacts système : hiberfil.sys, pagefile.sys, swapfile.sys à la racine d'un volume ; " +
            "C:\\Windows.old ; $Recycle.Bin par disque ; C:\\Windows\\SoftwareDistribution\\Download (cache Windows Update) ; " +
            "C:\\Windows\\Prefetch ; CrashDumps / Minidump / LiveKernelReports ; Delivery Optimization cache. Seuil 50 Mo.\n" +
            "  • Caches de dev : dossiers dont le nom matche node_modules, packages (.nuget), __pycache__, " +
            ".pytest_cache, .mypy_cache, .gradle, .cargo, venv, .venv, target (Rust/Maven), .tox, .next, .turbo, dist, build. " +
            "Seuil 50 Mo. Anti-double-comptage via suppression des descendants.\n" +
            "  • Caches navigateurs : dossiers Cache, cache2, Code Cache, GPUCache, Service Worker, ShaderCache, " +
            "GrShaderCache, Media Cache, Application Cache sous les chemins profile de Chrome/Edge/Firefox/Opera/Brave/Vivaldi. Seuil 20 Mo.",
        SafetyGuidance:
            "Les CACHES (dev + navigateurs) se régénèrent automatiquement à la prochaine utilisation — suppression SÛRE. " +
            "Les ARTEFACTS SYSTÈME demandent plus de nuance : " +
            "  - hiberfil.sys : ne PAS supprimer directement, utiliser 'powercfg /hibernate off' (sinon Windows le recrée) ;\n" +
            "  - pagefile.sys : même chose, configurer via Paramètres système avancés ;\n" +
            "  - Windows.old : SÛR après 10 jours post-upgrade (la fenêtre de rollback est passée), libère 15-30 Go typiquement ;\n" +
            "  - $Recycle.Bin : c'est la corbeille ; la vider = perdre définitivement ce qu'elle contient.\n" +
            "Pour node_modules/venv : vérifier qu'aucun IDE n'est ouvert sur le projet, sinon recréer = coût.",
        PerItemExtraLabel: "Catégorie DiskScout");

    public static readonly TabContext Duplicates = new(
        TabName: "Doublons",
        TabPurpose:
            "L'onglet Doublons liste les fichiers qui existent en plusieurs copies sur le disque. L'objectif est de garder " +
            "une seule copie et libérer le reste.",
        DetectionMethod:
            "Passe 1 (automatique) : groupage par (nom de fichier, taille en octets). Rapide mais peut produire des faux positifs — " +
            "deux fichiers 'config.json' de 2 Ko dans des projets différents sont rarement le même contenu.\n" +
            "Passe 2 (optionnelle, déclenchée par l'utilisateur) : vérification par hash xxHash3 en 2 étapes — " +
            "(a) hash partiel des 64 premiers Ko de chaque fichier dans un groupe, (b) hash complet des fichiers qui collisionnent " +
            "sur le partiel. Élimine ~100% des faux positifs. Les groupes marqués ✓ ont passé cette vérification.",
        SafetyGuidance:
            "RÈGLE D'OR : dans chaque groupe, GARDER au moins une copie. Supprimer toutes les copies = perte définitive.\n" +
            "Pour choisir laquelle garder :\n" +
            "  - Préférer la copie dans le chemin le plus 'officiel' (Documents, dossier utilisateur, dossier de projet source)\n" +
            "  - Éviter de supprimer une copie située dans un dossier d'installation de logiciel (Program Files, package.json deps)\n" +
            "  - Attention aux symlinks/hardlinks : deux chemins différents peuvent pointer sur la même inode (suppression d'un = suppression de l'autre)\n" +
            "  - Attention aux node_modules : les même fichiers peuvent être dupliqués légitimement entre projets\n" +
            "  - Les médias (photos, vidéos) sont les candidats les plus sûrs à dédupliquer.",
        PerItemExtraLabel: "Info groupe");

    public static readonly TabContext OldFiles = new(
        TabName: "Vieux fichiers",
        TabPurpose:
            "L'onglet Vieux fichiers liste les fichiers non modifiés depuis longtemps, filtrés par âge minimum et taille minimum, " +
            "regroupés par extension. L'objectif est d'archiver ou supprimer le contenu dormant.",
        DetectionMethod:
            "Filtre sur (LastWriteTime < aujourd'hui − N jours) ET (taille ≥ M Mo). Regroupement par extension de fichier. " +
            "Le timestamp est celui de Windows (FindFirstFileEx), donc reflète la dernière écriture — pas la dernière LECTURE.",
        SafetyGuidance:
            "L'âge seul ne suffit PAS pour juger la suppression. Critères à croiser :\n" +
            "  - Le CHEMIN est déterminant : un vieux fichier dans Documents / Desktop / Pictures est probablement précieux ; " +
            "le même sous AppData ou Downloads est plus disposable.\n" +
            "  - Les EXTENSIONS de projet (.rvt, .dwg, .ifc, .blend, .psd, .fcpx) représentent souvent du travail — " +
            "très vieux = archiver, PAS supprimer.\n" +
            "  - Les ARCHIVES (.zip, .tar, .rar, .7z) anciennes sont souvent des backups oubliés mais VITAUX en cas de besoin — MOYEN.\n" +
            "  - Les vieux .log, .tmp, .bak, .cache sont des candidats sûrs à supprimer.\n" +
            "  - SURTOUT : si le fichier est sous un dossier projet actif (contient .git, package.json, *.sln, pom.xml dans ses ancêtres), " +
            "ne PAS supprimer même s'il est vieux — il peut être référencé sans avoir été ouvert récemment.",
        PerItemExtraLabel: "Âge");
}

public static class AuditPromptBuilder
{
    public static bool BuildAndCopy(TabContext context, IReadOnlyList<AuditItem> items)
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

        var prompt = Build(context, items);
        try { Clipboard.SetText(prompt); }
        catch
        {
            MessageBox.Show("Impossible d'écrire dans le presse-papier. Ré-essaie.",
                "DiskScout", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        MessageBox.Show(
            $"Prompt pour {items.Count} élément(s) copié dans le presse-papier ({prompt.Length:n0} caractères).\n\n" +
            "Colle-le dans ChatGPT, Claude, Claude Cowork, Gemini, Mistral, Copilot, etc.\n\n" +
            "Le prompt contient :\n" +
            "  • La description de l'onglet « " + context.TabName + " » et sa méthode de détection\n" +
            "  • Les règles de sécurité spécifiques à cette catégorie\n" +
            "  • Chaque élément sélectionné avec son chemin, taille, raison\n\n" +
            "L'IA te renverra une analyse point par point + une synthèse en 3 buckets (sûr / à vérifier / à garder).",
            "DiskScout — Log IA",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return true;
    }

    public static string Build(TabContext context, IReadOnlyList<AuditItem> items)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# DiskScout — demande d'audit de suppression");
        sb.AppendLine();
        sb.AppendLine("Tu es expert en administration système Windows et en sécurité des données utilisateur.");
        sb.AppendLine("J'utilise DiskScout (outil d'analyse disque en lecture seule par défaut) pour nettoyer mon disque.");
        sb.AppendLine("Ton rôle : auditer ma sélection avant que je ne lance la suppression, pour m'éviter toute perte de données.");
        sb.AppendLine();

        sb.AppendLine("## Contexte de l'onglet : " + context.TabName);
        sb.AppendLine();
        sb.AppendLine("**À quoi sert cet onglet**");
        sb.AppendLine(context.TabPurpose);
        sb.AppendLine();
        sb.AppendLine("**Méthode de détection utilisée par DiskScout**");
        sb.AppendLine(context.DetectionMethod);
        sb.AppendLine();
        sb.AppendLine("**Règles de sécurité spécifiques à cette catégorie**");
        sb.AppendLine(context.SafetyGuidance);
        sb.AppendLine();

        sb.AppendLine("## Format de réponse attendu");
        sb.AppendLine();
        sb.AppendLine("Pour chaque élément listé ci-dessous, réponds STRICTEMENT dans ce format :");
        sb.AppendLine();
        sb.AppendLine("  [N] {nom du fichier ou dossier}");
        sb.AppendLine("      {emoji} — NIVEAU : {CRITIQUE / ÉLEVÉ / MOYEN / FAIBLE / AUCUN}");
        sb.AppendLine("      Raison : {1-2 phrases expliquant ce que fait ce fichier/dossier, pourquoi il peut ou ne peut pas être supprimé,");
        sb.AppendLine("                en croisant le chemin, le type, la catégorie DiskScout, et les règles de sécurité ci-dessus}");
        sb.AppendLine("      Action recommandée : {SUPPRIMER / CORBEILLE OK / VÉRIFIER AVANT / GARDER / NE PAS TOUCHER}");
        sb.AppendLine();
        sb.AppendLine("Barème de niveau :");
        sb.AppendLine("  🟢 AUCUN      — Suppression sûre, aucun risque");
        sb.AppendLine("  🟡 FAIBLE     — Contenu utilisateur non critique, réversible si souci");
        sb.AppendLine("  🟠 MOYEN      — Possiblement utile encore, à vérifier");
        sb.AppendLine("  🔴 ÉLEVÉ      — Configuration système, credentials, ou données actives");
        sb.AppendLine("  ⛔ CRITIQUE   — Binaire OS, driver, état d'app en cours, données chiffrées");
        sb.AppendLine();
        sb.AppendLine("Termine OBLIGATOIREMENT par une **synthèse finale** structurée en 3 buckets :");
        sb.AppendLine("  ### ✅ À supprimer sans risque");
        sb.AppendLine("  ### ⚠️ À vérifier avant de supprimer");
        sb.AppendLine("  ### 🚫 À ABSOLUMENT garder");
        sb.AppendLine();
        sb.AppendLine("Chaque bucket liste les numéros [N] concernés. Si un bucket est vide, écris « (rien) ».");
        sb.AppendLine();
        sb.AppendLine("Réponds en français, sois concret et actionnable.");
        sb.AppendLine();

        sb.AppendLine("## " + items.Count + " élément(s) à analyser");
        sb.AppendLine();

        int idx = 1;
        foreach (var it in items)
        {
            sb.Append('[').Append(idx++).Append("] ").AppendLine(it.FullPath);
            sb.Append("    Taille : ").AppendLine(FormatBytes(it.SizeBytes));
            if (!string.IsNullOrWhiteSpace(it.Reason))
            {
                sb.Append("    ").Append(context.PerItemExtraLabel ?? "Détection DiskScout").Append(" : ").AppendLine(it.Reason);
            }
            if (!string.IsNullOrWhiteSpace(it.Extra))
            {
                sb.Append("    Info : ").AppendLine(it.Extra);
            }
            sb.AppendLine();
        }

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
