# Requirements: DiskScout

**Defined:** 2026-04-24
**Core Value:** Montrer clairement où va l'espace disque et identifier les rémanents — sans jamais rien supprimer.

## v1 Requirements

### Platform

- [ ] **PLAT-01**: L'application démarre uniquement en mode administrateur (manifest `requireAdministrator`, prompt UAC au lancement)
- [ ] **PLAT-02**: L'application gère silencieusement `UnauthorizedAccessException`, `DirectoryNotFoundException`, `PathTooLongException` et `IOException` pendant le scan (logguées, scan continue)
- [ ] **PLAT-03**: L'application écrit ses logs dans `%LocalAppData%\DiskScout\diskscout.log` avec rotation automatique à 5 Mo (5 fichiers retenus)
- [ ] **PLAT-04**: L'application supporte les chemins longs via préfixe `\\?\` et manifest `longPathAware` (chemins > 260 caractères accessibles)

### Scan

- [ ] **SCAN-01**: L'utilisateur peut voir la liste des disques fixes locaux et sélectionner un ou plusieurs disques à scanner
- [ ] **SCAN-02**: L'utilisateur peut lancer un scan des disques sélectionnés via un bouton dans l'en-tête
- [ ] **SCAN-03**: L'utilisateur voit la progression du scan en temps réel (fichiers traités, Mo scannés, chemin courant) avec une barre de progression
- [ ] **SCAN-04**: L'utilisateur peut annuler un scan en cours via un bouton Annuler, avec libération propre des handles natifs sous 2 secondes
- [ ] **SCAN-05**: Le scan utilise P/Invoke `FindFirstFileEx` avec `FIND_FIRST_EX_LARGE_FETCH` et termine un disque de 500 Go sous 3 minutes sur SSD
- [ ] **SCAN-06**: Le scan détecte les reparse points (junctions, symlinks, OneDrive placeholders) et ne les suit pas récursivement (évite les boucles et les doublons de taille)
- [ ] **SCAN-07**: L'interface reste réactive pendant le scan (pas de freeze UI, progression throttlée à ~10 Hz)

### Programs

- [ ] **PROG-01**: Après scan, l'onglet Programmes liste tous les programmes installés lus depuis les registres `HKLM\...\Uninstall` (Registry64 + Registry32 via WOW6432Node) et `HKCU\...\Uninstall`
- [ ] **PROG-02**: Chaque ligne affiche : nom (DisplayName), éditeur (Publisher), date d'installation (InstallDate), chemin (InstallLocation), taille réelle (recalculée via scan du InstallLocation)
- [ ] **PROG-03**: L'utilisateur peut trier la liste par toutes les colonnes (nom, éditeur, taille, date) avec virtualisation DataGrid active
- [ ] **PROG-04**: L'utilisateur peut filtrer/rechercher dans la liste par nom ou éditeur

### Orphans

- [ ] **ORPH-01**: L'onglet Rémanents groupe les fichiers détectés par type : AppData orphelins, Program Files vides, Temp anciens, Installer patches orphelins
- [ ] **ORPH-02**: DiskScout détecte les dossiers orphelins dans `%LocalAppData%`, `%AppData%`, `%ProgramData%` via matching fuzzy en deux étapes (exact/préfixe sur nom de dossier, puis Jaccard sur tokens Publisher + DisplayName, seuil configurable, défaut 0.7)
- [ ] **ORPH-03**: DiskScout détecte les dossiers Program Files et Program Files (x86) considérés vides : taille < 1 Mo ou contenu limité à `.log`/`.txt`
- [ ] **ORPH-04**: DiskScout détecte les fichiers dans `%TEMP%` et `%LocalAppData%\Temp` non modifiés depuis plus de 30 jours
- [ ] **ORPH-05**: DiskScout détecte les patches MSI orphelins dans `Windows\Installer\*.msp` non référencés par un programme installé
- [ ] **ORPH-06**: Chaque entrée rémanente affiche le chemin complet, la taille, la raison de la détection (heuristique déclenchée) pour permettre à l'utilisateur de décider sans ambiguïté

### Tree

- [ ] **TREE-01**: L'onglet Arborescence affiche un TreeView hiérarchique de chaque disque scanné
- [ ] **TREE-02**: Chaque nœud affiche son nom, sa taille agrégée (bottom-up) et une barre proportionnelle visualisant sa part relative dans le parent
- [ ] **TREE-03**: Les enfants de chaque nœud sont triés par taille décroissante
- [ ] **TREE-04**: Le TreeView utilise la virtualisation WPF (`VirtualizingStackPanel` + `VirtualizationMode=Recycling`) et lazy-loading des nœuds enfants pour rester fluide avec 100k+ éléments
- [ ] **TREE-05**: L'utilisateur peut cliquer sur un nœud pour voir son chemin complet et naviguer dans l'arborescence

### Persistence

- [ ] **PERS-01**: Chaque scan est automatiquement sauvegardé en JSON dans `%LocalAppData%\DiskScout\scans\scan_YYYYMMDD_HHmmss.json` avec `schemaVersion: 1`
- [ ] **PERS-02**: Le JSON de persistance utilise une représentation plate (liste de nœuds avec `ParentId`) et non récursive (évite stack overflow sur trees 1M+ nœuds)
- [ ] **PERS-03**: L'utilisateur peut voir l'historique des scans passés dans l'UI et recharger un scan antérieur pour l'afficher

### Delta

- [ ] **DELT-01**: L'utilisateur peut sélectionner deux scans sauvegardés et lancer une comparaison
- [ ] **DELT-02**: La comparaison affiche quatre catégories : dossiers/fichiers apparus, disparus, ayant grossi, ayant réduit, via diff par dictionnaire path-keyed
- [ ] **DELT-03**: Chaque entrée du delta indique le delta de taille (`+500 Mo`, `-2 Go`) et le pourcentage de variation

### Export

- [ ] **EXPO-01**: L'utilisateur peut exporter le contenu de chacun des 3 onglets (Programmes / Rémanents / Arborescence) en CSV
- [ ] **EXPO-02**: L'utilisateur peut exporter le contenu de chacun des 3 onglets en HTML rendu via template Scriban
- [ ] **EXPO-03**: L'export CSV et HTML respecte les filtres et tris appliqués à la vue au moment de l'export

### Deployment

- [ ] **DEPL-01**: L'application se publie en single-file self-contained win-x64 via `dotnet publish` avec `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`
- [ ] **DEPL-02**: Le binaire résultant est portable (aucune installation, aucune dépendance runtime .NET sur la machine cible) et se lance directement depuis n'importe quel dossier

## v2 Requirements

Post-v1 — gardés en vue mais hors scope initial.

### Visualization

- **VIZ-01**: TreeMap (rectangles proportionnels type WinDirStat) en overlay de l'onglet Arborescence
- **VIZ-02**: Thème clair toggleable (le thème sombre est par défaut en v1)

### Scan Extensions

- **SCAN-V2-01**: Support des applications MSIX / UWP (via équivalent `Get-AppxPackage`) en plus des entrées Uninstall classiques
- **SCAN-V2-02**: Énumération des `HKU` de tous les utilisateurs pour orphelins multi-utilisateurs (pas seulement HKCU de l'utilisateur courant)

### Orphans Refinement

- **ORPH-V2-01**: Détection des orphelins de raccourcis Start Menu
- **ORPH-V2-02**: Détection des shell extensions orphelines (registre `HKLM\...\ShellEx`)
- **ORPH-V2-03**: Seuils configurables par l'utilisateur (âge Temp, taille Program Files vide, threshold Levenshtein/Jaccard)
- **ORPH-V2-04**: Colonne "Pourquoi flaggé" détaillée avec score de matching et candidat registre le plus proche

### UX Extensions

- **UX-V2-01**: Raccourcis clavier pour tri et navigation rapide
- **UX-V2-02**: Persistance de l'état UI (taille fenêtre, onglet actif, tri par colonne)
- **UX-V2-03**: Breakdown par extension de fichier (dans un onglet dédié ou overlay)
- **UX-V2-04**: Graphiques de tendance d'occupation disque dans le temps (à partir de plusieurs scans historiques)
- **UX-V2-05**: Mode CLI pour scripter des scans sans UI

### Platform v2

- **PLAT-V2-01**: Migration .NET 8 → .NET 10 LTS (à planifier avant EOL .NET 8 le 2026-11-10)
- **PLAT-V2-02**: Migration du schéma JSON `schemaVersion` avec lecture rétrocompatible des anciens scans

## Out of Scope

Exclusions explicites — documentées pour prévenir la dérive de scope.

| Feature | Reason |
|---------|--------|
| Suppression de fichiers ou dossiers | Règle produit absolue : lecture seule. DiskScout analyse, l'utilisateur supprime ailleurs. Éliminé comme classe de bug destructeur. |
| Cloud sync / API externe / télémétrie | 100 % local par design. Pas de dépendance réseau, pas de compte, pas d'envoi de données. |
| Multi-utilisateur simultané / mode serveur | Usage personnel mono-machine. |
| Support Linux / macOS | Stack Windows-only (WPF, registre, P/Invoke Win32). |
| Installation MSI / MSIX / setup | Portable `.exe` uniquement ("pose, lance, zappe"). |
| Moteur antivirus ou détection malware | Hors scope produit. |
| Scan de partages réseau (SMB, UNC) | v1 se limite aux disques fixes locaux. Réseau potentiellement v2+ selon feedback. |
| Chat / collaboration / partage de scans | Outil personnel, aucune dimension sociale. |

## Traceability

Rempli lors de la création de ROADMAP.md.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PLAT-01 | — | Pending |
| PLAT-02 | — | Pending |
| PLAT-03 | — | Pending |
| PLAT-04 | — | Pending |
| SCAN-01 | — | Pending |
| SCAN-02 | — | Pending |
| SCAN-03 | — | Pending |
| SCAN-04 | — | Pending |
| SCAN-05 | — | Pending |
| SCAN-06 | — | Pending |
| SCAN-07 | — | Pending |
| PROG-01 | — | Pending |
| PROG-02 | — | Pending |
| PROG-03 | — | Pending |
| PROG-04 | — | Pending |
| ORPH-01 | — | Pending |
| ORPH-02 | — | Pending |
| ORPH-03 | — | Pending |
| ORPH-04 | — | Pending |
| ORPH-05 | — | Pending |
| ORPH-06 | — | Pending |
| TREE-01 | — | Pending |
| TREE-02 | — | Pending |
| TREE-03 | — | Pending |
| TREE-04 | — | Pending |
| TREE-05 | — | Pending |
| PERS-01 | — | Pending |
| PERS-02 | — | Pending |
| PERS-03 | — | Pending |
| DELT-01 | — | Pending |
| DELT-02 | — | Pending |
| DELT-03 | — | Pending |
| EXPO-01 | — | Pending |
| EXPO-02 | — | Pending |
| EXPO-03 | — | Pending |
| DEPL-01 | — | Pending |
| DEPL-02 | — | Pending |

**Coverage:**
- v1 requirements: 37 total
- Mapped to phases: 0 (à remplir par roadmapper)
- Unmapped: 37 ⚠️

---
*Requirements defined: 2026-04-24*
*Last updated: 2026-04-24 after initial definition*
