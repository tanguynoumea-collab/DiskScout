# Phase 9 — Programs Tab Real Uninstaller Assistant

## Goal

Transformer l'onglet Programmes (actuellement scan registre `Uninstall` en lecture seule) en assistant complet de désinstallation, inspiré de **Revo Uninstaller Pro** et **BCUninstaller**.

## Strategic Decisions (validated by user — do not re-discuss)

1. **Source programmes** — étendre le scan registre actuel avec **tracking temps réel des installations** (logger FileSystem + Registry pendant qu'un installeur tourne, à la Revo Pro)
2. **Niveau d'agressivité** — **Revo Pro** : désinstalleur natif piloté + deep scan résidus + règles spécifiques par éditeur
3. **Modèle de suppression** — **suppression directe** (pas via la quarantaine 30 j existante ; la quarantaine reste pour les autres workflows DiskScout)

## Deliverables

### Services
- **`InstallTracker`** — `FileSystemWatcher` + Registry watch (`RegNotifyChangeKeyValue` ou WMI) pour capturer les fichiers/clés créés pendant un install. Trace persistée en JSON par installation pour usage futur lors du désinstall.
- **`NativeUninstallerDriver`** — lit `UninstallString`/`QuietUninstallString` depuis `HKLM\...\Uninstall` + `HKCU\...\Uninstall`, exécute en silent quand possible (MSI `/qn`, flags éditeur), avec progress + cancellation.
- **`ResidueScanner`** (post-désinstall) :
  - Registre : `HKLM`/`HKCU`/`HKCR` — `Uninstall`, `App Paths`, `CLSID`, `Software\<Publisher>`, `RunOnce`
  - Filesystem : `Program Files (x86)`, `AppData/Local/Roaming`, `ProgramData`, `Public`, `Temp`
  - Raccourcis Démarrer/Bureau/Public Desktop
  - MSI orphelins (`C:\Windows\Installer`)
  - Services Windows résiduels, tâches planifiées résiduelles
  - Extensions shell (ContextMenuHandlers, etc.)
- **`PublisherRuleEngine`** — règles JSON par éditeur (Adobe, Autodesk, JetBrains, Mozilla, Microsoft, Steam, Epic, etc.) avec patterns connus de résidus. Les règles sont **embarquées** dans l'exe + extensibles via `%LocalAppData%\DiskScout\publisher-rules\`.

### UI — Wizard multi-étapes dans l'onglet Programmes
1. **Sélection** programme (DataGrid existant + ajout colonnes : "Tracé ?" si install tracker a une trace, "Règles éditeur ?" si publisher a une règle)
2. **Preview résidus connus** (avant désinstall — depuis trace + règles éditeur)
3. **Run native uninstaller** (progress + log temps réel)
4. **Scan résidus post-uninstall** (cumulé : trace + règles + scan profond)
5. **Confirmation suppression résidus** (arborescence cochable, taille libérée projetée)
6. **Rapport final** (HTML/JSON exportable)

### Suppression
Vrai `DELETE` (pas Move). Réutiliser `FileDeletionService` existant en mode **permanent** (pas quarantaine).

## Technical Constraints

- WPF .NET 8, MVVM strict (CommunityToolkit.Mvvm)
- `requireAdministrator` déjà actif (manifest)
- DI manuelle dans `App.xaml.cs` (pas de container)
- `async/await` + `IProgress<T>` + `CancellationToken` partout
- Logging Serilog déjà en place
- P/Invoke OK quand nécessaire (déjà utilisé pour `FindFirstFileEx`)
- Pas de dépendance externe runtime supplémentaire si possible (single-file ~85 Mo actuel)

## Inspiration Sources (à étudier en research-phase)

| Source | Priorité | Pourquoi |
|--------|----------|----------|
| **BCUninstaller** ([Klocman/Bulk-Crap-Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller)) | **HIGH** | Open-source, **C#/WPF** (même stack !), code lisible, deep scan + junk finder mature |
| **Revo Uninstaller Pro** | HIGH | Référence du tracker temps réel et format trace logs |
| **IObit Uninstaller** | MEDIUM | UX du deep scan + presentation résidus |
| **Geek Uninstaller** | LOW | UX minimaliste pour inspiration wizard |

## Suggested Plan Decomposition (le planner pourra splitter en plusieurs phases)

1. **Install Tracker** — service + persistance trace JSON
2. **Native Uninstaller Driver** — lecture UninstallString + exécution avec progress
3. **Residue Scanner** — scan registre + filesystem + services/tasks
4. **Publisher Rule Engine** — format règles JSON + bundling + matcher
5. **Wizard UI** — refonte onglet Programmes en flow multi-étapes
6. **Integration + Report** — assemblage end-to-end + export rapport

## Risks to Address in Plan

- **Perte de données** : un faux positif sur le scan résidus = fichier utilisateur supprimé. Whitelist agressive + confirmation explicite obligatoire.
- **Performance** : le tracker FS sur tout le système pendant un install peut générer 100k+ events. Filtrage au niveau watcher + buffering.
- **Permissions** : certains résidus sont sous TrustedInstaller — détecter et signaler plutôt que tenter de forcer.
- **Désinstall natif planté** : timeout + détection de processus zombie + fallback "désinstall manuel".
- **Compatibilité 32/64 bits** : registre Wow6432Node, dossiers Program Files (x86).

## Notes

- L'utilisateur a déjà refusé la quarantaine pour ce flow → pas de filet de sécurité côté DiskScout. La confirmation modale est donc **critique**.
- Le rapport final doit être exportable car c'est une preuve utile en cas de support / forensique.
