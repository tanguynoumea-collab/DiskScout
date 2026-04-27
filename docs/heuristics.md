# Heuristiques de détection des rémanents AppData

> Documentation de référence du pipeline « AppData orphelin » — version Phase 10 (avril 2026).

DiskScout détecte les dossiers **rémanents** (résidus de programmes désinstallés) sous `C:\ProgramData`, `%LocalAppData%` et `%AppData%`. Cette page explique **comment** la détection fonctionne, **pourquoi** un dossier obtient tel ou tel score, et **comment l'utilisateur peut adapter** les règles à sa machine.

---

## Pourquoi cette refonte ?

Avant Phase 10, l'algorithme était simple : pour chaque dossier dans `AppData`, vérifier qu'au moins un programme installé matche le nom du dossier (Levenshtein > 0.7) ; si non → orphelin.

**Mesure utilisateur sur un parc HP corporate (`C:\ProgramData`, 365 items audités à la main) :**

- > 90 % de **faux positifs** (composants OS, agents corporate, caches MSI proposés à la suppression).
- 0 % de **vrais résidus** réellement supprimables sans risque.

Phase 10 vise **< 5 % FP** sans dégrader le rappel sur les vrais résidus. Le moyen : un pipeline 7 étapes qui croise plusieurs sources (registre, services, drivers, paquets Appx, alias éditeurs) avant de proposer un score de confiance.

---

## Pipeline en 7 étapes

```
Candidat (chemin) ──▶ [1] HardBlacklist ─── match ─────▶ EXCLUDE (jamais émis)
                            │
                            ▼ no match
                     [2] ParentContextAnalyzer ── normalise au parent significatif
                            │ (si leaf ∈ {Logs, Cache, Settings, Updates,
                            │   en-us, fr-fr, …})
                            ▼
                     [3] KnownPathRules ── match path pattern ─▶ marque catégorie
                            │ (PackageCache, DriverData, CorporateAgent,
                            │  VendorShared, Generic)
                            ▼
                     [4] MultiSourceMatcher (parallèle, Task.WhenAll)
                            ├── RegistryMatcher (HKLM\…\Uninstall)
                            ├── ServiceMatcher (Get-Service)
                            ├── DriverMatcher (Get-WindowsDriver)
                            └── AppxMatcher (Get-AppxPackage)
                            ▼
                     [5] PublisherAliasResolver (Revit ↔ RVT, BCF Manager ↔ BcfManager…)
                            ▼
                     [6] ConfidenceScorer (combine signaux + ancienneté + taille)
                            ▼
                     [7] RiskLevelClassifier ──▶ AUCUN / FAIBLE / MOYEN / ELEVE / CRITIQUE
                                              + RecommendedAction (Supprimer, CorbeilleOk,
                                                                   VerifierAvant, NePasToucher)
                                              + MinRiskFloor clamp (PackageCache→Eleve, etc.)
```

Chaque étape est un service injectable (manual DI dans `App.xaml.cs`). Les étapes 4-5 tirent un `MachineSnapshot` partagé (TTL 5 min) pour éviter de relancer les commandes PowerShell à chaque candidat.

---

## Étape 1 — HardBlacklist

Si le chemin candidat **commence par** un pattern de catégorie `OsCriticalDoNotPropose`, le pipeline retourne `null` immédiatement : **le candidat n'est jamais affiché** dans l'onglet Rémanents.

Exemples (extraits de `Resources/PathRules/os-critical.json`) :

| Pattern | Justification |
|---------|---------------|
| `%WinDir%\System32` | Composant OS |
| `%WinDir%\WinSxS` | Side-by-side store géré par TrustedInstaller |
| `%WinDir%\Installer` | Cache MSI Windows requis par tous les MSI installés |
| `%WinDir%\SoftwareDistribution` | Cache Windows Update |
| `%ProgramData%\Microsoft\Crypto` | Magasins cryptographiques |
| `%ProgramData%\Microsoft\Vault` | Credentials Windows |
| `%ProgramData%\Microsoft\Windows Defender` | Antivirus système |
| `%ProgramData%\Microsoft\WinGet` | Gestionnaire de paquets |
| `%ProgramData%\Autodesk\Adlm` | Autodesk License Manager |
| `%ProgramData%\Autodesk\AdSSO` | Autodesk Single Sign-On |
| `%ProgramData%\Microsoft\ClickToRun` | Cache Office C2R partagé |
| `%ProgramData%\Microsoft OneDrive` | Installation partagée OneDrive |
| `%ProgramData%\FLEXnet` | Licences Adobe / Autodesk |
| `%ProgramData%\Package Cache` | Cache Burn/WiX (Visual C++ Redist, etc.) |
| `%ProgramData%\Packages` | Appx package store |
| `%ProgramData%\Microsoft\VisualStudio` | Caches VS partagés |
| `%ProgramData%\regid.` | SWID tags ISO/IEC 19770-2 |
| `%ProgramFiles%\WindowsApps` | Appx install root |
| `%ProgramFiles%\Common Files` | Composants partagés |

Au total, ~70 entrées dans `os-critical.json`. **Ces chemins ne sont jamais proposés** — DiskScout les ignore comme s'ils n'existaient pas.

---

## Étape 2 — ParentContextAnalyzer

Un dossier qui s'appelle `Logs`, `Cache`, `Settings`, `en-us`, `Components` etc. ne porte aucun signal éditeur — il faut **remonter au parent significatif**.

**Liste des leaves « insignifiants »** (extrait) :
`Logs, Cache, Settings, Updates, Download(s), Installer(s), Components, sym, Symbols, jsonoutput, output, storage, scripting, uscripting, policy, tzdata, Common, Images, LangResources, Resources`, ainsi que les codes de locale (`en-us, fr-fr, de-de, es-es, ja-jp`, …).

**Exemple :**
```
C:\ProgramData\Autodesk\Revit 2024\Logs\error.log
                     ↑              ↑
                     parent         leaf insignifiant
                     significatif
```

Le matcher de l'étape 4 utilisera **`Revit 2024`** (et non `Logs`) pour vérifier qu'un programme installé matche.

---

## Étape 3 — KnownPathRules

Si le chemin matche un pattern d'une des 5 catégories ci-dessous, la `PathCategory` est attachée au candidat et un éventuel `MinRiskFloor` est noté.

| Catégorie | MinRiskFloor | Comportement |
|-----------|:------------:|--------------|
| `OsCriticalDoNotPropose` | — | Voir étape 1 (suppression du candidat). |
| `PackageCache` | `Eleve` | Cache MSI / VSIX / setup — jamais Supprimer / CorbeilleOk auto. |
| `DriverData` | — | Pas de plancher ; le matcher Driver fournit déjà le signal. |
| `CorporateAgent` | `Eleve` | Anti-virus, RMM, EDR, VPN — jamais auto-proposer. |
| `VendorShared` | — | Workspace partagé éditeur ; score classique. |
| `Generic` | — | Match catch-all (rule fournie sans sémantique forte). |

**Exemples par catégorie** (extraits des JSONs déployés) :

- **PackageCache** : `%ProgramData%\Package Cache`, `%ProgramData%\Microsoft\VisualStudio\Packages`, `%ProgramData%\Microsoft\VisualStudio\Setup`, `%WinDir%\Installer`, `%ProgramData%\Microsoft OneDrive\setup`.
- **DriverData** : `%ProgramData%\Intel`, `%ProgramData%\NVIDIA`, `%ProgramData%\NVIDIA Corporation`, `%ProgramData%\AMD`, `%ProgramData%\Realtek`.
- **CorporateAgent** : `%ProgramData%\NinjaRMMAgent`, `%ProgramData%\Bitdefender`, `%ProgramData%\Zscaler`, `%ProgramData%\Matrix42`, `%ProgramData%\Empirum`, `%ProgramData%\Splashtop`, `%ProgramData%\CrowdStrike`.
- **VendorShared** : `%ProgramData%\Adobe`, `%ProgramData%\Autodesk`, `%ProgramData%\JetBrains`, `%ProgramData%\Microsoft OneDrive`.

`MinRiskFloor=Eleve` signifie : **même si tous les matchers sortent à zéro et que le score atteint 100**, la classification finale est forcée à **`Eleve`** (≈ NePasToucher). C'est la garantie qu'on ne propose **jamais** la suppression d'un Bitdefender même si l'utilisateur l'a désinstallé : un cache résiduel reste utile pour une réinstallation, et la dangerosité opérationnelle (perte d'antivirus, cassure d'agent corporate) prime sur la libération d'espace.

---

## Étape 4 — MultiSourceMatcher

Le pipeline interroge en parallèle (via `Task.WhenAll`) les 4 sources suivantes pour le **parent significatif** issu de l'étape 2 :

| Source | Outil sous-jacent | Évidence cherchée |
|--------|-------------------|--------------------|
| `RegistryMatcher` | `HKLM\…\Uninstall` + `HKCU\…\Uninstall` | DisplayName / Publisher matche le nom de dossier |
| `ServiceMatcher` | `IServiceEnumerator` (Get-Service / WMI) | Un service Windows référence le chemin candidat |
| `DriverMatcher` | `IDriverEnumerator` (Get-WindowsDriver / pnputil) | Un pilote signé référence le chemin |
| `AppxMatcher` | `IAppxEnumerator` (Get-AppxPackage) | Un paquet UWP / MSIX porte le nom de l'éditeur |

Chaque hit produit un `MatcherHit { Source, Evidence, ScoreDelta }`.

> **NOTE Phase 10.x** — Un `ProcessMatcher` (vérifier qu'un processus en cours référence le binaire) est explicitement reporté. Les 4 matchers actuels couvrent ~95 % des cas en pratique (un binaire actif est presque toujours associé à un service ou tâche planifiée).

---

## Étape 5 — PublisherAliasResolver

Le matcher de registre échoue souvent à cause des **alias éditeurs** : `RVT 2025` ne matche pas `Autodesk Revit 2025`, `BcfManager` ne matche pas `BCF Managers 6.5 - Revit 2021 - 2024 6.5.5`, etc.

Le résolveur d'alias charge `aliases.json` (embedded + user merge par `canonical`, last-write-wins) et propose 3 stratégies en cascade :

1. **Exact** sur l'alias canonique → score 1.0
2. **Préfixe** sur un alias listé → score 0.9
3. **Token Jaccard** sur les noms tokenisés → score 0.5+ (seuil interne 0.6)

Quelques alias embedded :

```json
{ "canonical": "Revit",       "aliases": ["RVT", "Autodesk Revit", "Revit Architecture", "Revit MEP", "Revit LT"], "type": "product" }
{ "canonical": "BCF Manager", "aliases": ["BcfManager", "BCF Managers", "KUBUS BCF"], "type": "product" }
{ "canonical": "Visual Studio", "aliases": ["VS", "VisualStudio", "Microsoft Visual Studio"], "type": "product" }
{ "canonical": "Navisworks",  "aliases": ["NW", "Navisworks Manage", "Navisworks Simulate"], "type": "product" }
```

---

## Étape 6 — ConfidenceScorer

**Score initial = 100** (probable résidu). Chaque signal applique un delta cumulatif. Score final clampé `[0, 100]`.

### Pénalités matcher (signaux d'activité)

| Signal | Delta |
|--------|------:|
| Match registre | -50 |
| Service en cours d'exécution (Running) | -60 |
| Service arrêté (Stopped) | -30 |
| Driver présent | -45 |
| Paquet Appx installé | -50 |
| Processus actif (futur, déféré) | -40 |
| Tâche planifiée active | -30 |

### Pénalités catégorie (KnownPathRules)

| Catégorie | Delta |
|-----------|------:|
| `OsCriticalDoNotPropose` | -100 (forcé Critique) |
| `PackageCache` | -90 |
| `DriverData` | -70 |
| `CorporateAgent` | -80 |
| `VendorShared` | -50 |

### Bonus résidu (signes positifs)

| Critère | Delta |
|---------|------:|
| Dossier vide (taille 0) | +20 |
| Dernier accès > 365 j | +15 |
| Dernier accès > 180 j | +10 |
| Pas de fichier `.exe` ni `.dll` | +10 |

> **NOTE** — Le `ServiceMatcher` actuel utilise `-45` (moyenne entre Running/Stopped) car `IServiceEnumerator` n'expose pas encore l'état du service. À affiner en Phase 10.x (TODO documenté dans `ServiceMatcher.cs`).

---

## Étape 7 — RiskLevelClassifier

| Score | RiskLevel | RecommendedAction | UI Badge |
|------:|-----------|-------------------|---------|
| ≥ 80 | `Aucun` | `Supprimer` | vert |
| 60-79 | `Faible` | `CorbeilleOk` | citron |
| 40-59 | `Moyen` | `VerifierAvant` | orange |
| 20-39 | `Eleve` | `NePasToucher` | orange foncé |
| < 20 | `Critique` | `NePasToucher` | rouge |

Si le candidat est passé par une `PathCategory` avec un `MinRiskFloor`, le RiskLevel est **clampé vers le haut** :

```
score=95, minRiskFloor=Eleve  →  Risk=Eleve, Action=NePasToucher
                                  (et non Aucun / Supprimer)
```

C'est l'invariant **« on ne dégrade jamais la sécurité par un score haut »**.

---

## Comment ajouter une règle utilisateur

DiskScout cherche les règles **utilisateur** (en plus des règles embedded) dans :

```
%LocalAppData%\DiskScout\path-rules\
```

Tout fichier `.json` placé dans ce dossier est chargé. Le merge se fait par **`Id`, last-write-wins** : une règle utilisateur avec le même `Id` qu'une règle embedded **remplace** cette dernière.

### Exemple : ajouter mon antivirus interne au plancher Eleve

Crée `%LocalAppData%\DiskScout\path-rules\my-corp-rules.json` :

```json
[
  {
    "id": "corp-agent-myantivirus",
    "pathPattern": "%ProgramData%\\MyCompanyAntivirus",
    "category": "CorporateAgent",
    "minRiskFloor": "Eleve",
    "reason": "Antivirus interne ACME — agent corporate, MinRiskFloor=Eleve."
  }
]
```

Au prochain scan, tout chemin commençant par `C:\ProgramData\MyCompanyAntivirus` aura un badge orange foncé et la mention « Pourquoi ? » expliquera la règle déclenchée.

### Tokens disponibles dans `pathPattern`

- `%WinDir%`, `%ProgramData%`, `%ProgramFiles%`, `%ProgramFiles(x86)%`, `%LocalAppData%`, `%AppData%` — variables d'environnement Windows.
- Le matching est **case-insensitive** et **prefix-based**.

### Override d'une règle embedded

Pour faire passer Bitdefender de `Eleve` à `Generic` (à vos risques) :

```json
[
  {
    "id": "corp-agent-bitdefender",
    "pathPattern": "%ProgramData%\\Bitdefender",
    "category": "Generic",
    "minRiskFloor": null,
    "reason": "Override utilisateur — Bitdefender n'est plus considéré corporate."
  }
]
```

L'`Id` doit matcher **exactement** celui de la règle embedded. Voir la liste des Ids dans `src/DiskScout.App/Resources/PathRules/*.json`.

---

## Comment ajouter un alias éditeur

> **v1.2** : non disponible. `aliases.json` est embedded only. Un futur `%LocalAppData%\DiskScout\aliases\user-aliases.json` est planifié pour v1.3.

Pour l'instant, si vous identifiez un alias manquant, ouvrez une issue avec :
- Le nom de dossier observé (ex. `XYZ Inc`)
- Le DisplayName / Publisher du programme installé associé (ex. `XYZ International Corp.`)

---

## FAQ

### J'ai marqué X comme `Aucun` (sûr) mais Phase 10 dit `Critique` — comment forcer la suppression ?

Écrivez une règle utilisateur ciblant exactement ce chemin avec `category: "Generic"` :

```json
[
  {
    "id": "user-override-mon-cache",
    "pathPattern": "C:\\ProgramData\\Vendor\\OldCache",
    "category": "Generic",
    "minRiskFloor": null,
    "reason": "Override utilisateur — j'assume la suppression."
  }
]
```

Cela contourne la règle embedded mais **ne supprime pas** le HardBlacklist (étape 1). Si le pattern est listé dans `os-critical.json`, vous devez d'abord override l'`Id` de cette entrée.

> ⚠ DiskScout reste **lecture seule** — il ne supprime jamais. Vous supprimez vous-même via l'Explorateur ou PowerShell après lecture du score.

### Pourquoi `Package Cache` ne descend-il jamais en `Aucun` même si vide ?

Parce que le `MinRiskFloor=Eleve` est plus fort que le score. Un cache MSI vide aujourd'hui peut être ré-utilisé par le désinstallateur d'un programme dans 6 mois — le supprimer **casse irrémédiablement** la désinstallation propre. Le surcoût disque est typiquement < 200 Mo, le risque opérationnel est élevé.

### Pourquoi le badge dit `Critique` pour un dossier qui semble vide ?

Probablement parce qu'un matcher (registre, service, driver, Appx) a trouvé une référence active. Cliquez sur **« Pourquoi ? »** pour voir lequel.

Exemple typique : `%ProgramData%\Vendor\App\Logs` est vide, mais le service Windows `VendorAppSvc` pointe vers `%ProgramData%\Vendor\App\` → le RegistryMatcher / ServiceMatcher fait un hit sur le **parent significatif** (étape 2), score chute à -50 ou moins.

### Le scan est plus lent qu'avant — pourquoi ?

Phase 10 indexe la machine au premier scan de chaque session : ~1.5 à 3 s pour collecter Services + Drivers + Appx + ScheduledTasks. Le `MachineSnapshot` est ensuite cached 5 minutes (TTL), donc les scans suivants paient zéro surcoût. Cible : **+30 % max** vs Phase 3.

---

## Corpus de référence

La précision du moteur est mesurée contre un **corpus fixture de 365 items** collectés par audit manuel sur une machine HP corporate (`C:\ProgramData`, ~71 % CRITIQUE, ~12 % ELEVE, ~10 % MOYEN, ~6 % FAIBLE, ~1 % AUCUN).

Test d'intégration : `RemanentDetector_Should_Match_Manual_Audit` (livré par Plan 10-05).

**Critères d'acceptation :**

- ≥ 95 % des verdicts du moteur matchent le verdict humain (à un cran d'écart près).
- 0 % d'items classés `CRITIQUE` par l'humain proposés en `Supprimer` ou `CorbeilleOk` par le moteur.

La concordance mesurée et les éventuels écarts sont consignés dans le résumé du plan 10-05 :
[10-05-SUMMARY.md](../.planning/phases/10-orphan-detection-precision-refactor/10-05-SUMMARY.md).

---

## Mode `--audit`

DiskScout supporte un mode batch sans UI :

```powershell
DiskScout.exe --audit
```

Il scanne, applique le pipeline et exporte un CSV ouvrable avec Excel :

```
%LocalAppData%\DiskScout\audits\audit_YYYYMMDD_HHmmss.csv
```

Colonnes : `Path, SizeBytes, ConfidenceScore, RiskLevel, TriggeredRules, ExonerationRules, RecommendedAction`.

Utile pour :
- Vérifier la concordance contre votre propre audit.
- Comparer deux machines.
- Alimenter un script PowerShell d'analyse.

---

## Hors scope

- **UI deletion flow** — toujours géré par le wizard de désinstallation (Phase 9), pas par l'onglet Rémanents.
- **MFT scanner** — accès raw à la Master File Table, reporté à Phase B (post-v1).
- **`%LocalAppData%` corpus** — l'audit fixture couvre `ProgramData` car c'est là que les FP sont les plus dangereux. Audit `LocalAppData` planifié v1.x.

---

## Lectures complémentaires

- `.planning/phases/10-orphan-detection-precision-refactor/10-CONTEXT.md` — décisions d'architecture.
- `.planning/phases/10-orphan-detection-precision-refactor/10-04-SUMMARY.md` — détails du pipeline.
- `src/DiskScout.App/Resources/PathRules/*.json` — règles embedded (source de vérité).
- `src/DiskScout.App/Services/AppDataOrphanPipeline.cs` — implémentation du pipeline 7 étapes.
- `src/DiskScout.App/Services/ConfidenceScorer.cs` — détail des deltas de score.
- `src/DiskScout.App/Services/RiskLevelClassifier.cs` — détail des bandes Risk.
