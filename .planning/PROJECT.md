# DiskScout

## What This Is

Outil Windows desktop (WPF/.NET 8) d'analyse intelligente de l'occupation disque pour usage personnel mono-utilisateur. Scanne les disques fixes en lecture seule et présente trois vues complémentaires : programmes installés (via registre) avec taille réelle, fichiers rémanents de programmes désinstallés (AppData orphelins, Program Files vides, Temp anciens, patches MSI orphelins), et arborescence hiérarchique triée par taille décroissante. Se lance en portable single-file `.exe` sans installation.

## Core Value

Montrer clairement où va l'espace disque, identifier les rémanents, et permettre la suppression ciblée via corbeille ou, sur confirmation explicite, de manière définitive.

## Requirements

### Validated

<!-- Shipped and confirmed valuable. -->

(None yet — ship to validate)

### Active

<!-- Current scope. Building toward these. -->

- [ ] Scanner les disques fixes sélectionnés en lecture seule avec progression live
- [ ] Lister tous les programmes installés (registre HKLM 64/WOW6432/HKCU) avec taille réelle calculée depuis InstallLocation
- [ ] Détecter les fichiers rémanents (AppData orphelins, Program Files vides, Temp > 30 jours, installer patches orphelins)
- [ ] Afficher l'arborescence hiérarchique triée par taille décroissante avec lazy-loading
- [ ] Persister chaque scan en JSON versionné dans `%LocalAppData%\DiskScout\scans\`
- [ ] Comparer deux scans pour afficher delta (apparu / disparu / grossi / réduit)
- [ ] Exporter les résultats des 3 onglets en CSV et HTML
- [ ] Annuler un scan en cours proprement (CancellationToken + libération handles natifs)
- [ ] Publier en single-file self-contained win-x64 (portable `.exe`, zéro dépendance runtime)

### Out of Scope

<!-- Explicit boundaries. Includes reasoning to prevent re-adding. -->

- ~~Toute forme de suppression de fichier ou dossier~~ — règle levée le 2026-04-24 : suppression possible via clic droit, corbeille par défaut, définitif sur confirmation explicite ; chaque action est loggée dans Serilog
- Cloud, API externe, télémétrie — 100 % local, pas de dépendance réseau
- Multi-utilisateur ou mode serveur — usage personnel sur une seule machine
- Support Linux / macOS — WPF + registre Windows + P/Invoke Win32 = Windows only
- Installation MSI / setup — portable `.exe` uniquement ("pose, lance, zappe")
- Moteur antivirus ou détection malware — pas le scope produit

## Context

**Domaine :** outil d'inspection disque grand public dans la lignée de WinDirStat / TreeSize / WizTree, avec la particularité d'un couplage registre-installés ↔ détection rémanents que les concurrents ne fournissent pas nativement.

**Environnement technique :** Windows 10+ desktop, .NET 8, WPF. Target hardware : SSD et HDD locaux, scan typique d'un disque de 500 Go attendu sous 3 minutes sur SSD.

**Privilèges :** l'application doit tourner en administrateur pour accéder à `Program Files`, `Windows`, `ProgramData` et les `AppData` des autres utilisateurs. Déclaré dans le manifest (`requireAdministrator`).

**Perf critique :** `Directory.EnumerateFiles` est trop lent pour un scan de cette taille. Bascule obligatoire sur P/Invoke `FindFirstFileEx` avec flag `FIND_FIRST_EX_LARGE_FETCH` + parallélisation `Parallel.ForEach` au premier niveau + agrégation bottom-up.

## Constraints

- **Tech stack** : WPF + C# .NET 8 + CommunityToolkit.Mvvm + System.Text.Json — aucune dépendance externe runtime, tout embarqué dans le single-file
- **Architecture** : MVVM stricte (Models / Views / ViewModels / Services / Helpers), pas de code-behind sauf événements UI purs (drag/drop, resize)
- **Perf** : scan d'un disque de 500 Go utilisable sous 3 min sur SSD ; UI jamais bloquée (async/await + IProgress partout)
- **Safety** : suppression autorisée uniquement via `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory` avec dialog de confirmation et audit Serilog. Corbeille par défaut, définitif sur double-confirmation. Jamais de suppression bulk sans confirmation individuelle.
- **Robustesse** : `UnauthorizedAccessException`, `DirectoryNotFoundException`, `PathTooLongException`, `IOException` loggés et ignorés, scan continue
- **Privilèges** : manifest `requireAdministrator` obligatoire
- **Dépendances** : injection manuelle dans `App.xaml.cs`, pas de conteneur DI externe
- **Déploiement** : `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
- **Logging** : fichier rotatif 5 Mo max dans `%LocalAppData%\DiskScout\diskscout.log`
- **Persistance** : JSON versionné (`schemaVersion: 1`) dans `%LocalAppData%\DiskScout\scans\scan_YYYYMMDD_HHmmss.json`
- **Workflow dev** : après chaque build réussi d'une phase produisant un état lançable, lancer automatiquement le prototype (`dotnet run` ou `./DiskScout.exe`) pour validation visuelle immédiate par l'utilisateur

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| WPF + .NET 8 plutôt que WinUI 3 / MAUI | Stack mature, tooling stable, single-file self-contained simple, pas besoin de packaging MSIX | — Pending |
| CommunityToolkit.Mvvm plutôt que Prism ou ReactiveUI | Moins de boilerplate, `[ObservableProperty]` + `[RelayCommand]` suffisent, zéro dépendance runtime | — Pending |
| P/Invoke `FindFirstFileEx` plutôt que `Directory.EnumerateFiles` | Perf critique sur gros disques, flag `LARGE_FETCH` divise le temps de scan | — Pending |
| Lecture seule absolue — aucun `Delete*` dans le code | Sécurité produit : élimine toute classe de bug destructeur, l'utilisateur supprime lui-même | — Pending |
| Single-file self-contained plutôt que framework-dependent | "Pose, lance, zappe" — aucune installation .NET requise sur la machine cible | — Pending |
| Injection manuelle dans `App.xaml.cs` plutôt que `Microsoft.Extensions.DependencyInjection` | Pas besoin de conteneur DI pour une app de cette taille, garde le binaire léger | — Pending |
| Fuzzy matching Levenshtein (seuil 0.7) pour orphelins AppData | Nom de dossier AppData diverge souvent de `DisplayName` registre (casse, espaces, suffixes) | — Pending |
| Persistance JSON versionnée (`schemaVersion: 1`) | Permet migrations futures du schéma sans casser les anciens scans | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-04-24 after initialization*
