# Phase 10: Orphan Detection Precision Refactor - Context

**Gathered:** 2026-04-27
**Status:** Ready for planning
**Mode:** Auto-generated (decisions pre-locked by user prior to invoking autonomous workflow)

<domain>
## Phase Boundary

Refondre le moteur de détection des rémanents AppData (l'heuristique « AppData orphelin » de l'onglet Rémanents) pour passer d'un taux de faux positifs > 90 % à < 5 % sans dégrader le rappel sur les vrais résidus, **sans toucher** aux trois autres heuristiques de [OrphanDetectorService](../../src/DiskScout.App/Services/OrphanDetectorService.cs) (Program Files vides, Temp anciens, MSI orphelins) ni aux catégories transverses (system artifacts, browser caches, dev caches, empty folders, broken shortcuts).

**Pipeline cible — 7 étapes :**

```
Candidat (chemin) ──▶ [1] HardBlacklist ─── match ─────▶ EXCLUDE
                            │
                            ▼ no match
                     [2] ParentContextAnalyzer ── normalise (parent significatif)
                            │
                            ▼
                     [3] KnownPathRules ── match path pattern ─▶ marque type
                            │
                            ▼
                     [4] MultiSourceMatcher (parallèle)
                            ├── RegistryMatcher
                            ├── ServiceMatcher
                            ├── DriverMatcher
                            ├── AppxMatcher
                            ├── ProcessMatcher
                            └── ScheduledTaskMatcher
                            ▼
                     [5] PublisherAliasResolver
                            ▼
                     [6] ConfidenceScorer (combine signaux + ancienneté + taille)
                            ▼
                     [7] RiskLevelClassifier ──▶ AUCUN / FAIBLE / MOYEN / ELEVE / CRITIQUE
```

**Out of scope :** UI deletion flow (déjà couvert par Phase 9 wizard), MFT scanner (Phase B post-v1), CLI mode (Phase B), heuristiques (2)/(3)/(4) intactes.

</domain>

<decisions>
## Implementation Decisions

### Architecture & Stack

- **Format des règles :** JSON embarqué via `<EmbeddedResource>` (cohérence avec `Resources/PublisherRules/*.json` existant — aucune dépendance YAML ajoutée).
- **Nouveau moteur :** `PathRuleEngine` parallèle au `PublisherRuleEngine` existant (les deux moteurs cohabitent — `PublisherRule` reste utilisé par le wizard, `PathRule` est consommé par le nouveau pipeline AppData).
- **Modèle `PathRule` unique** couvrant **HardBlacklist** ET **KnownPathRules** : un `PathRule` avec `Category=OsCriticalDoNotPropose` force `risk=CRITIQUE` + `action=NePasToucher` (= HardBlacklist) ; les autres catégories (`PackageCache`, `DriverData`, `CorporateAgent`, `VendorShared`, etc.) servent de KnownPathRules avec `MinRiskFloor` déclaratif.
- **Chargement embedded + user merge** par `Id` last-write-wins (pattern strictement identique à `PublisherRuleEngine.LoadAsync`).

### Réutilisation de l'infrastructure existante

- **Promouvoir** `IServiceEnumerator` et `IScheduledTaskEnumerator` de `internal` (actuellement dans `ResidueScanner.cs`) à **`public`** dans `Services/IServiceEnumerator.cs` et `Services/IScheduledTaskEnumerator.cs`.
- **Ajouter** `IDriverEnumerator` (`Get-WindowsDriver -Online` ou `pnputil /enum-drivers`) et `IAppxEnumerator` (`Get-AppxPackage -AllUsers`) selon le même pattern (interfaces publiques, impls package-private, test seams).
- **Implémentations concrètes** restent package-private — seules les interfaces sont exposées.

### Modèle de données

- **Nouveau record `AppDataOrphanCandidate`** :
  ```csharp
  public sealed record AppDataOrphanCandidate(
      long NodeId,
      string FullPath,
      long SizeBytes,
      DateTime LastWriteUtc,
      string ParentSignificantPath,
      PathCategory Category,
      IReadOnlyList<MatcherHit> MatchedSources,
      IReadOnlyList<RuleHit> TriggeredRules,
      int ConfidenceScore,
      RiskLevel Risk,
      RecommendedAction Action,
      string Reason);
  ```
- `OrphanCandidate` existant **inchangé** (rétro-compat pour les 8 autres catégories).
- `OrphanDetectorService.DetectAsync` continue de retourner `IReadOnlyList<OrphanCandidate>` ; le pipeline AppData convertit en `OrphanCandidate` à la sortie pour rester compatible avec la VM. Les champs étendus de `AppDataOrphanCandidate` sont optionnellement attachés à `OrphanCandidate` via une propriété `Diagnostics` ajoutée (rétro-compat : null pour les 8 autres catégories).

### Performance

- **`MachineSnapshot` lazy + TTL 5 min**, scoped à un `IMachineSnapshotProvider.GetAsync(CancellationToken)`.
- **Indexation parallèle** des 4 sources (Services / Drivers / Appx / ScheduledTasks) via `Task.WhenAll`.
- Cible : surcoût scan ≤ +30 % vs version actuelle (~ 1.5–3 s d'indexation au premier appel par session).

### Safety floor déclaratif

- Tout `PathRule` avec `Category=PackageCache` → `MinRiskFloor=Eleve` (jamais `Supprimer` ou `CorbeilleOk` automatique, même si tous les matchers sortent à zéro).
- Tout `PathRule` avec `Category=OsCriticalDoNotPropose` → exclu immédiatement par HardBlacklist (jamais émis).
- Tout `PathRule` avec `Category=CorporateAgent` → `MinRiskFloor=Eleve` (Bitdefender, NinjaRMM, Zscaler, Matrix42, etc.).

### Corpus de référence

- Fixture `tests/fixtures/programdata_corpus_365.json` contenant les **365 items** de l'audit utilisateur (déjà fournis dans le prompt initial, avec verdict humain par item : `Aucun` / `Faible` / `Moyen` / `Eleve` / `Critique` + `RecommendedAction`).
- Test d'intégration `RemanentDetector_Should_Match_Manual_Audit` :
  - **≥ 95 %** des verdicts du moteur correspondent au verdict humain (à un cran d'écart près).
  - **0 %** d'items classés `CRITIQUE` par l'humain proposés en `Supprimer` ou `CorbeilleOk` par le moteur.

### Mode `--audit`

- Argument CLI ajouté à l'app (parse dans [App.xaml.cs](../../src/DiskScout.App/App.xaml.cs) `OnStartup`).
- Scanne sans afficher l'UI, produit un CSV ouvrable Excel avec colonnes : `Path, SizeBytes, ConfidenceScore, RiskLevel, TriggeredRules, ExonerationRules, RecommendedAction`.
- Sortie dans `%LocalAppData%\DiskScout\audits\audit_YYYYMMDD_HHmmss.csv`.

### UI

- Nouvelle colonne `Score` dans la DataGrid de l'onglet Rémanents (badge coloré selon `RiskLevel`).
- Tooltip sur chaque ligne listant les règles déclenchées.
- Bouton « Pourquoi ? » ouvrant un panneau détaillé avec : règles déclenchées, matchers consultés, calcul du score étape par étape.

### Claude's Discretion

- Découpe interne en services injectables dans `App.xaml.cs` (manual DI pattern existant).
- Choix entre stratégie « walk parent » itérative ou récursive dans `ParentContextAnalyzer`.
- Stratégie de fuzzy alias (`PublisherAliasResolver`) — token-based ou regex.
- Couleurs précises des badges UI (réutiliser palette existante de [HealthTabView](../../src/DiskScout.App/Views/Tabs/HealthTabView.xaml)).
- Implémentation concrète des enumerators (PowerShell shell-out vs WMI vs natif P/Invoke) — privilégier P/Invoke / .NET BCL si disponible pour la perf.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets

- **[PublisherRuleEngine](../../src/DiskScout.App/Services/PublisherRuleEngine.cs)** — pattern complet de chargement embedded + user merge par Id à reproduire pour `PathRuleEngine`. Inclut regex timeout, env var expansion, `{Token}` substitution.
- **[ResiduePathSafety](../../src/DiskScout.App/Helpers/ResiduePathSafety.cs)** — 19 substrings filesystem + 17 prefixes registry + 15 service names critiques. À enrichir mais ne pas remplacer (utilisé par le wizard de désinstallation).
- **[ResidueScanner](../../src/DiskScout.App/Services/ResidueScanner.cs)** — contient déjà `IServiceEnumerator` + `IScheduledTaskEnumerator` à promouvoir public.
- **[FuzzyMatcher](../../src/DiskScout.App/Helpers/FuzzyMatcher.cs)** — fuzzy string matching réutilisable par `PublisherAliasResolver`.
- **[AppPaths](../../src/DiskScout.App/Helpers/AppPaths.cs)** — gère les dossiers user data — y ajouter `AuditFolder` et `PathRulesFolder`.

### Established Patterns

- **Manual DI dans [App.xaml.cs](../../src/DiskScout.App/App.xaml.cs)** — services instanciés au startup, injectés par constructeur. À étendre pour les 4 nouveaux matchers + `PathRuleEngine` + `IMachineSnapshotProvider`.
- **`<EmbeddedResource>` JSON** — pattern documenté pour `PublisherRules`. À répliquer pour `PathRules` et `aliases.json`.
- **Tests xUnit + FluentAssertions + Moq** — 130/130 tests passants actuellement. Pas de test pour `OrphanDetectorService` (gap à combler). Tests existants référence : `PublisherRuleEngineTests.cs`, `ResidueScannerTests.cs`, `FuzzyMatcherTests.cs`, `ResiduePathSafetyTests.cs`.
- **`<EmbeddedResource Include="Resources\PublisherRules\*.json" />`** dans `.csproj` — vérifier qu'un pattern similaire est déjà exposé pour les nouveaux résolveurs.

### Integration Points

- **[OrphanDetectorService.DetectAsync](../../src/DiskScout.App/Services/OrphanDetectorService.cs#L60)** — la branche AppData (lignes 128-139) est le seul point modifié. Les autres branches (system artifacts, browser/dev caches, empty folders, broken shortcuts, Program Files vides, Temp anciens, MSI orphelins) restent intactes.
- **[OrphansViewModel](../../src/DiskScout.App/ViewModels/OrphansViewModel.cs)** — consomme `IReadOnlyList<OrphanCandidate>`. Si le modèle de sortie reste `OrphanCandidate` (avec champ `Diagnostics?` optionnel), zéro impact côté VM ; sinon adapter le type.
- **[OrphansTabView](../../src/DiskScout.App/Views/Tabs/OrphansTabView.xaml)** — lieu où ajouter colonne Score, tooltip et bouton « Pourquoi ? ».
- **[App.xaml.cs:62-64](../../src/DiskScout.App/App.xaml.cs#L62)** — instanciation actuelle : `IOrphanDetectorService orphanDetectorService = new OrphanDetectorService(_logger);`. À étendre.

</code_context>

<specifics>
## Specific Ideas

**Corpus de référence (déjà disponible dans la conversation utilisateur initiale, à matérialiser en fixture JSON) :**
- 365 items collectés par audit manuel sur `C:\ProgramData` d'une machine de production (HP corporate avec NinjaRMM + Zscaler + Matrix42 + Empirum + Bitdefender + Splashtop + suite Autodesk + Office 365 + Microsoft Teams + Claude Desktop + Python Manager + WhatsApp UWP).
- Distribution mesurée : ~71 % CRITIQUE (composants OS + caches MSI + credentials), ~12 % ELEVE (corporate agents, drivers, applications actives), ~10 % MOYEN (caches VC++ Redist), ~6 % FAIBLE (logs vides), ~1 % AUCUN (vrais résidus).
- Tous les chemins commencent par `C:\ProgramData\` — un cas (`[14] C:\Users\tanguy.delrieu\AppData\LocalLow`) déborde sur AppData utilisateur.

**Pipeline implementation hints (issus du prompt utilisateur) :**

- ParentContextAnalyzer remonte au parent significatif si le leaf est dans la liste `{Logs, Cache, Settings, Updates, Download(s), Installer(s), Components, sym, Symbols, jsonoutput, output, storage, scripting, uscripting, policy, tzdata, Common, Images, LangResources, Resources, locale codes (en-us, fr-fr, de-de, es-es, etc.)}`.

- ConfidenceScorer (score initial 100 = probable résidu) :
  - Registre matcher : −50
  - Service Windows actif (Running) : −60
  - Service Windows arrêté : −30
  - Driver présent : −45
  - Appx package installé : −50
  - Processus en cours : −40
  - Tâche planifiée active : −30
  - `KnownPathRules.OsComponent` : −100 (forcé `CRITIQUE`)
  - `KnownPathRules.PackageCache` : −90
  - `KnownPathRules.DriverData` : −70
  - `KnownPathRules.CorporateAgent` : −80
  - `KnownPathRules.VendorShared` : −50
  - Bonus résidu :
    - Dossier vide (taille 0) : +20
    - Dernier accès > 365 j : +15
    - Dernier accès > 180 j : +10
    - Pas de fichier .exe/.dll : +10

- RiskLevel mapping :
  - Score ≥ 80 : `Aucun` (suppression sûre)
  - Score 60-79 : `Faible`
  - Score 40-59 : `Moyen` (`VerifierAvant`)
  - Score 20-39 : `Eleve`
  - Score < 20 : `Critique` (`NePasToucher`)

</specifics>

<deferred>
## Deferred Ideas

- **`ProcessMatcher`** (matcher [4]/processus en cours) reporté à v1.x si la perf de `Process.GetProcesses()` dépasse 500 ms : les autres matchers couvrent déjà 90 % des cas (un binaire chargé en RAM est presque toujours associé à un service ou tâche planifiée).
- **Corpus complémentaire `%LocalAppData%`** : audit similaire sur `C:\Users\<user>\AppData\Local` reporté à v1.x. La phase actuelle vise `ProgramData` car c'est là que les FP sont les plus dangereux (composants OS + corporate).
- **Apprentissage automatique des aliases** (mining sur le registre Uninstall pour générer `aliases.json` automatiquement) reporté à v2.

</deferred>
