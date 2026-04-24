# DiskScout — Audit de cohérence

**Date :** 2026-04-24
**Périmètre :** 11 onglets, 18 services, ~50 fichiers VM/View, 3 docs stratégiques.
**Objectif :** identifier les incohérences, fonctionnalités orphelines, et dérives avant le prochain sprint.

---

## 1. Résumé exécutif

DiskScout a grandi par couches successives. Le produit **fonctionne et impressionne**, mais 4 classes de dettes se sont accumulées :

| Dette | Nb items | Gravité |
|---|---|---|
| **Code orphelin** (services instanciés jamais consommés) | 5 | ⚠️ élevée |
| **Incohérences UX** (features asymétriques entre onglets) | 9 | ⚠️ moyenne |
| **Docs désynchronisées** (roadmap vs réalité, guide décrit features non livrées) | 4 | 🟡 basse |
| **Dette test** (nouvelles features non couvertes) | 8 | 🟡 basse |

**Top 3 actions prioritaires :**
1. Brancher les 5 features orphelines OU les retirer du code (au choix)
2. Étendre le pattern Expander + checkboxes + Log IA aux 3 onglets restants
3. Centraliser la palette (couleurs hardcodées dans XAML) et la persistance des préférences utilisateur

---

## 2. Code orphelin (code instancié mais jamais utilisé)

### 2.1 🔴 `CsvHtmlExporter` ne voit jamais de scan
```csharp
// App.xaml.cs:57
ScanResult? lastScan = null;
IExporter exporter = new CsvHtmlExporter(() => lastScan);
```
`lastScan` est une variable locale jamais réassignée. Résultat : si un bouton d'export CSV/HTML était ajouté un jour, il trouverait toujours `null` et crasherait.

**Aussi :** `MainViewModel` reçoit `IExporter exporter` puis fait `_ = exporter;` — l'injection est littéralement jetée.

**Fix :** soit câbler proprement (provider `() => _lastResult` comme pour le PDF), soit retirer l'injection et supprimer `CsvHtmlExporter` jusqu'au moment où on ajoute le bouton UI. Actuellement : 200 lignes de code mort.

### 2.2 🔴 `IDeltaComparator` injecté mais jamais appelé
```csharp
// MainViewModel.cs:44,53
IDeltaComparator deltaComparator,
...
_ = deltaComparator;  // dead
```
Le comparateur (Path-keyed dictionary diff, Phase 7 de la roadmap historique) n'est appelé nulle part. Pas d'UI pour comparer 2 scans.

**Fix :** livrer l'onglet "Delta / Historique" (utilise aussi `IPersistenceService.ListAsync`) OU retirer temporairement.

### 2.3 🔴 `IPersistenceService.ListAsync / LoadAsync` jamais appelés
```
$ grep ListAsync ViewModels/ → 0 matches
```
Les scans sont sauvegardés dans `%LocalAppData%\DiskScout\scans\scan_*.json` à chaque run. Mais **aucun UI** ne les liste, les recharge, ou ne les compare. Accumulation silencieuse de JSONs de 1 Go+ sur le disque.

**Fix :** livrer un onglet "Historique des scans" OU ajouter une purge automatique (>N scans = supprimer le plus ancien).

### 2.4 🔴 `FileSafety.Score` jamais consulté
`Helpers/FileSafety.cs` calcule un score 0-100 (0=OS critique, 10=projet actif avec .git, 95=cache régénérable). **Aucun VM ne l'utilise.**

**Fix :** consommer dans `PurgeSelectedAsync` — si un élément sélectionné a `Score ≤ 10`, le remonter en haut du dialog de confirmation avec un warning rouge. OU retirer le fichier.

### 2.5 🔴 `QuarantineService` jamais invoqué par `FileDeletionService`
L'onglet Quarantaine fonctionne pour **restaurer** mais le flux de suppression classique ne passe JAMAIS par quarantaine : tout va directement en corbeille Windows (ou définitif).

**Attendu** (d'après USER-GUIDE.md) : un mode "suppression sûre" qui envoie vers quarantaine DiskScout (30 j rétention contrôlée) au lieu de la corbeille Windows (qui peut être vidée à l'insu de l'utilisateur).

**Fix :** ajouter un 3e choix dans `DeletePrompt.Ask` : **Quarantaine DiskScout** / Corbeille Windows / Définitif. Router vers `IQuarantineService.MoveToQuarantineAsync` si choisi.

---

## 3. Incohérences UX entre onglets

### 3.1 Pattern Expander + checkboxes + batch + Log IA — **asymétrique**

| Onglet | Expanders | Checkboxes | Purger sélection | 🤖 Log IA | Verdict |
|---|---|---|---|---|---|
| Rémanents | ✅ | ✅ | ✅ | ✅ | ✅ complet |
| Nettoyage | ✅ | ✅ | ✅ | ✅ | ✅ complet |
| Doublons | ✅ | ✅ | ✅ | ✅ | ✅ complet |
| Vieux fichiers | ✅ | ✅ | ✅ | ✅ | ✅ complet |
| **Gros fichiers** | ❌ | ❌ | ❌ | ❌ | 🔴 **DataGrid à l'ancienne** |
| **Arborescence** | ❌ | ❌ | ❌ | ❌ | 🔴 clic-droit par nœud seulement |
| **Carte** | ❌ | ❌ | ❌ | ❌ | 🔴 clic-droit par rectangle seulement |
| Programmes | N/A | N/A | N/A | N/A | ⚪ pas de delete (normal) |
| Santé | N/A | N/A | N/A | N/A | ⚪ vue agrégée (normal) |
| Quarantaine | ❌ | ❌ | ❌ | N/A | 🟡 pas de batch restore |

**Action :** étendre à Gros fichiers, Arborescence, Carte, Quarantaine (au moins le batch). L'Arborescence en particulier est le flux principal — **l'absence de checkbox + Log IA y est le plus gros gap**.

### 3.2 `OrphansViewModel` sert 2 onglets sémantiquement différents
Classe réutilisée pour **Rémanents** et **Nettoyage** via un filtre `acceptedCategories`. L'astuce marche mais le nom trompe. Si on rajoute des features spécifiques à l'un (ex: "Nettoyage" ajoute un bouton "Appliquer la recommandation Windows"), il faudra dupliquer.

**Action :** rebaptiser `OrphansViewModel` → `CategorizedDeletionViewModel` ou extraire une base abstraite `CategorizedDeletionViewModelBase<TRow>`. Non urgent.

### 3.3 Wiring ContextMenu — 3 patterns différents cohabitent
```xml
<!-- Pattern A (Tree) : via Style.Setter ContextMenu -->
<Setter Property="ContextMenu">
    <Setter.Value>
        <ContextMenu>
            <MenuItem ... Command="{Binding PlacementTarget.DataContext.X,
                RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}" />

<!-- Pattern B (LargestFiles DataGrid) : DataGrid.ContextMenu directe -->
<DataGrid.ContextMenu>
    <ContextMenu>
        <MenuItem ... CommandParameter="{Binding PlacementTarget.SelectedItem, ...}" />

<!-- Pattern C (OrphansTabView) : pas de ContextMenu (a été remplacé par les checkboxes) -->
```

**Action :** documenter le pattern choisi (A pour tree-items, B pour DataGrid) dans un commentaire, ou extraire dans des styles partagés.

### 3.4 Boutons destructifs inline — couleur hardcodée
```xml
<!-- DuplicatesTabView, OrphansTabView, OldFilesTabView, LargestFilesTabView : -->
<Button ... Background="#FFC0392B" BorderBrush="#FFE74C3C" />
```
4 copies de la même couleur hex. Devrait être un brush dans `DarkTheme.xaml` :
```xml
<SolidColorBrush x:Key="DangerBrush" Color="#FFC0392B" />
<SolidColorBrush x:Key="DangerBorderBrush" Color="#FFE74C3C" />
```

**Action :** extraire 2 brushes + un style `PurgeButtonStyle` réutilisable.

### 3.5 Emojis dans le texte — non systématiques
- `🤖 Log IA` dans 4 onglets
- `↑ Remonter` dans Carte
- Aucun emoji ailleurs

**Action :** soit systématiser (icône dédiée à chaque action), soit retirer. Actuellement : aléatoire.

### 3.6 Filtres résistent pas au rescan
Sur Tree (`MinSizeGb`), OldFiles (`MinAgeDays`, `MinMbFilter`), Duplicates (`MinMbFilter`) : les valeurs par défaut reviennent à chaque nouveau scan. L'utilisateur doit re-saisir ses préférences.

**Action :** `UserSettingsService` qui persiste dans `%LocalAppData%\DiskScout\settings.json`.

### 3.7 Taille/position fenêtre, onglet actif, choix de disque — rien ne persiste
Même problème. Relancer = tout reconfigurer.

**Action :** même settings service, + sauvegarde à la fermeture.

### 3.8 Log IA — onglet Gros fichiers n'en a pas
Incohérent avec la promesse "transposable à tous les onglets" livrée au commit précédent. Manque aussi sur Arborescence et Carte.

**Action :** 3 VMs à étendre + 3 XAML (facile si Gros fichiers passe en Expander par dossier parent ou extension).

### 3.9 Duplicates — les "faux positifs" identifiés par hash ne sont pas mémorisés
Si on clique **Vérifier par hash**, les groupes faux-positifs disparaissent. Mais si on relance le scan, ils reviennent. Coût 100% à chaque cycle.

**Action :** cache hash → fichier (path + mtime + size + hash) dans `%LocalAppData%\DiskScout\hashes.db`. À la 2e passe, skip les fichiers non modifiés depuis.

---

## 4. Divergences code/style

### 4.1 Langue : FR côté UI, EN côté code/log
Pattern correct (code en anglais = portable, UI en français = cible utilisateur). **Mais** certains messages passent dans les logs en français via `_logger.Information("Scan saved to {Path}", ...)` — cohérent avec Serilog mais mélange les deux.

**Décision à prendre :** standardiser les logs en anglais. Actuellement : mix.

### 4.2 Formatage bytes — 4 implémentations
Le helper `FormatBytes(long bytes)` est dupliqué dans :
- `DeletePrompt.FormatBytes` (public)
- `AuditPromptBuilder.FormatBytes` (private)
- Chaque `*Row` a son `SizeDisplay` qui re-calcule
- Chaque `*Group` a son `FormatBytes` privé
- `BytesFormatConverter` (IValueConverter pour XAML)

**Action :** garder un SEUL helper `ByteFormat.Fmt(long)` et un converter. Retirer les 8 copies.

### 4.3 Constructeur MainViewModel — 11 paramètres
```csharp
public MainViewModel(
    ILogger logger,
    IDriveService driveService,
    IFileSystemScanner fileSystemScanner,
    IInstalledProgramsScanner installedProgramsScanner,
    IOrphanDetectorService orphanDetectorService,
    IPersistenceService persistenceService,
    IDeltaComparator deltaComparator,    // jeté
    IExporter exporter,                   // jeté
    IFileDeletionService fileDeletionService,
    IQuarantineService quarantineService,
    IPdfReportService pdfReport)
```

**Action :** introduire un `MainViewModelDependencies` record ou migrer vers `Microsoft.Extensions.DependencyInjection` (décision roadmap reportée → à retrancher si on veut scale).

### 4.4 Tests — 17 pass, mais quelle couverture ?
Tests couvrent : domain models JSON roundtrip, FuzzyMatcher, NativeFileSystemScanner (4 tests).
**Pas de tests pour :**
- OrphanDetectorService (9 heuristiques !)
- QuarantineService (move / restore / purge)
- FileDeletionService (batch, recycle vs permanent)
- PathKeyedDeltaComparator
- PdfReportService (même un smoke test)
- AuditPromptBuilder (format des prompts)
- DocumentTypeAnalyzer
- Treemap squarify
- Selection state sur les 4 VMs refactorés
- FileSafety.Score

**Action :** cibler 30-40 tests au total. Priorité : OrphanDetector (critique) + QuarantineService (dangereux si bug).

---

## 5. Docs désynchronisées

### 5.1 `USER-GUIDE.md` décrit un onglet Cloud qui a été retiré
Plus aucune mention de Cloud mais le guide a été écrit avant la suppression.

**Action :** vérifier/mettre à jour.

### 5.2 `USER-GUIDE.md` section Quarantaine dit :
> "Future évolution : le système de quarantaine se déclenchera sur un profil suppression sûre"

Ce "future évolution" est décrit comme **futur** dans le guide, mais la quarantaine EST déjà implémentée. Par contre elle n'est PAS routée automatiquement depuis `FileDeletionService`. Donc la description est **à moitié vraie** — le service existe, le câblage manque.

**Action :** soit câbler (cf 2.5) et mettre à jour le guide en présent, soit clarifier "prêt à être câblé".

### 5.3 `MARKET-AUDIT.md` roadmap Phase A
Phase A = Treemap + Doublons hash + Quarantine + ETA + Multi-select + Pause/Resume + Recherche globale + Batch ops.
- Treemap ✅ (livré + fix hang)
- Hash dedup ✅
- Quarantine ✅ (service + tab, mais pas câblé au flux delete — cf 2.5)
- ETA + débit ✅
- Multi-select via checkboxes ✅ (4 tabs sur 7 pertinents)
- Pause/Resume ❌ NON LIVRÉ
- Recherche globale (Ctrl+F) ❌ NON LIVRÉE
- Batch ops ✅ (même 4 tabs)

**Action :** MARKET-AUDIT.md doit marquer les ✅ / ❌ à jour.

### 5.4 `PROJECT.md` Core Value
Mentionne "suppression possible via clic droit, corbeille par défaut, définitif sur confirmation explicite" — correct, mais **ne mentionne pas** : Log IA, quarantaine, PDF export, analyse 11 types doc. Désynchronisé.

**Action :** rafraîchir PROJECT.md en fin de sprint.

---

## 6. Risques de données

### 6.1 Scans JSON s'accumulent
Chaque scan 1 M+ nœuds = ~1 Go JSON. 10 scans = 10 Go de stockage DiskScout lui-même. Jamais purgé.

**Fix :** garder les N derniers scans (defaut 10), purger le plus ancien.

### 6.2 Quarantaine s'accumule si pas de purge automatique à la fermeture
Purge auto au **démarrage** seulement. Si l'utilisateur garde DiskScout ouvert 40 jours (improbable mais possible), les sessions expirées ne sont pas nettoyées.

**Fix :** Timer quotidien en plus du startup.

### 6.3 `IsLikelySafeToSuggest` pas utilisé → on peut proposer de supprimer `C:\Windows\System32\xxx.dll` si par accident il matche un pattern de cache
Pas arrivé en pratique (les patterns sont stricts) mais `FileSafety.Score` existe pour ça. Le brancher est une sécurité zéro coût.

---

## 7. Performance

### 7.1 Treemap — fix appliqué, mais re-rendu complet sur drill-down
Pas de cache des layouts de sous-arbres. Drill-down = recalcule tout. Acceptable actuellement mais sur 4 To NTFS avec la migration MFT future, ça ramera.

**Action (plus tard) :** cache de layouts par `(rootId, size)`.

### 7.2 FindFirstFileEx reste le goulot
3 min / 500 Go SSD. Migration MFT prévue Phase B roadmap — **c'est LE différenciateur contre WizTree**, à prioriser.

---

## 8. Sécurité / robustesse

### 8.1 Clipboard.SetText en try/catch silencieux
Pattern copié/collé partout. Si le clipboard est bloqué, l'utilisateur n'a aucun retour.

**Action :** logger un warning Serilog à chaque échec pour savoir si c'est fréquent.

### 8.2 Pas de code signing
L'exe n'est pas signé Authenticode → SmartScreen va bloquer à l'installation externe, gros frein adoption (mentionné dans MARKET-AUDIT Phase D).

### 8.3 PDF Export — si le chemin est inaccessible (OneDrive offline), exception non gracieuse
try/catch existe mais génère un MessageBox — acceptable.

---

## 9. Plan d'action consolidé (pour le prochain sprint)

### Sprint de cohérence (1 semaine estimée)

**Priorité 1 — Câblage des features orphelines** (2 jours)
1. Router FileDeletionService via IQuarantineService (nouveau 3e choix dans DeletePrompt)
2. Consommer FileSafety.Score dans PurgeSelectedAsync (warning visuel si score ≤ 10)
3. Purger scan JSON > 10 fichiers à chaque sauvegarde
4. Retirer `lastScan = null` orphan + injection `IExporter` inutile ou câbler un bouton Export CSV/HTML
5. Retirer ou câbler `IDeltaComparator` (nouveau onglet "Historique / Delta")

**Priorité 2 — Uniformisation UX** (2 jours)
1. Étendre pattern Expander+checkboxes+LogIA à **Gros fichiers** (groupement par extension ou dossier parent)
2. Ajouter checkboxes + batch delete + LogIA à **Arborescence**
3. Ajouter LogIA à **Carte** (sur les rectangles sélectionnés)
4. Extraire `DangerBrush` + `PurgeButtonStyle` partagés
5. `UserSettingsService` pour persister filtres + window state

**Priorité 3 — Tests et docs** (1 jour)
1. Tests OrphanDetectorService (1 par catégorie = 9 tests)
2. Tests QuarantineService (move / restore / purge)
3. Mettre à jour USER-GUIDE.md (retirer Cloud, clarifier Quarantaine, ajouter Log IA)
4. Mettre à jour MARKET-AUDIT.md Phase A status
5. Rafraîchir PROJECT.md avec les features livrées

**Priorité 4 — Nettoyage** (½ journée)
1. Consolider les `FormatBytes` en un seul helper `ByteFormat`
2. Regrouper les constants de couleurs dans DarkTheme.xaml
3. Documenter les 2 patterns ContextMenu choisis

---

## 10. Ce qui va déjà bien (à préserver)

- **MVVM discipline** : code-behind minimaliste, services injectés par constructeur — lisible.
- **Thème sombre complet** : toutes les primitives WPF (DataGrid, ScrollBar, Menu, Button, CheckBox, Expander) ont leur ControlTemplate. Pas de fuites de chrome système.
- **Audit trail Serilog** : chaque suppression, chaque action critique loggée. Rotation en place.
- **Safety rails suppression** : corbeille par défaut + double confirmation pour définitif. Pas de régression.
- **P/Invoke soigné** : SafeFindHandle, long paths, cloud reparse points gérés. Base solide avant migration MFT.
- **Persistance JSON versionnée** : `schemaVersion: 1` posé, forward-compatible.
- **Logo et chrome pro** : icône multi-résolution + header identifiable + palette cohérente.

---

*DiskScout est à un vrai tournant : le produit est fonctionnellement dense ; 1 semaine de cohérence + câblage des orphelins, et on passe de "prototype riche" à "produit pro".*
