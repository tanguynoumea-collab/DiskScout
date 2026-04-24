# DiskScout — Audit concurrentiel et feuille de route pro

**Date :** 2026-04-24
**Objectif :** faire de DiskScout l'outil d'analyse et de nettoyage disque de référence sur Windows.

---

## 1. État actuel (résumé honnête)

**Ce que DiskScout fait déjà bien :**

| Domaine | Maturité | Détail |
|---|---|---|
| Scan local NTFS | ⚠️ correct | `FindFirstFileEx` + `LARGE_FETCH`, parallélisé root-level. ~3 min / 500 Go SSD. |
| Registre programmes | ✅ bon | 4 vues (HKLM x64/x86, HKCU x64/x86). |
| Détection rémanents | ✅ bon | 4 heuristiques historiques (AppData fuzzy, Program Files vides, Temp >30j, MSI orphelins). |
| Artefacts système | ✅ bon | hiberfil, pagefile, Windows.old, $Recycle.Bin, Prefetch, CrashDumps, cache Windows Update. |
| Caches dev | ✅ bon | 15 patterns (node_modules, .nuget, .gradle, .cargo, venv, target, etc.). |
| Caches navigateurs | ✅ bon | 6 navigateurs, 9 leaf names. |
| Onglet Santé | ✅ bon | Score/grade + actions recommandées priorisées. |
| Arborescence | ✅ bon | Lazy-load, filtre Go, heatmap d'âge, analyse 11 types. |
| Doublons | ⚠️ faible | Nom+taille seulement. Pas de hash. Beaucoup de faux positifs. |
| Suppression | ✅ bon | Corbeille par défaut, définitif sur double confirmation, audit log. |
| OneDrive/SharePoint | ✅ solide | Physique vs logique correctement séparés via `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`. |
| Visualisation | ❌ manquant | **Pas de treemap**, pas de sunburst, pas de graphique de composition global. |
| Vitesse | ❌ en retard | **10-20× plus lent que WizTree**, qui lit le MFT directement. |
| Export | ⚠️ minimal | CSV et HTML basique ; pas de PDF, pas d'Excel mis en forme. |
| CLI / automation | ❌ manquant | Pas d'interface ligne de commande, pas de scheduler, pas d'API. |

---

## 2. Paysage concurrentiel — benchmark feature-par-feature

### Les 6 références du marché

| Outil | Prix | Force principale | Faiblesse |
|---|---|---|---|
| **WizTree** | Gratuit/Pro 20 € | **Scan NTFS via MFT — ~20 s pour 4 To** | UI basique, pas de cleanup, pas de recommandations |
| **TreeSize Pro** | 60 € solo / 200 € site | Fonctionnalités pro (duplicates hash, scheduled scans, export Excel/PDF, CLI, remote scan) | Cher, UX vieillissante |
| **WinDirStat** | Gratuit OSS | **Treemap canonique**, file-type coloring | Scan lent (> 10 min/500 Go), plus de dev actif |
| **SpaceSniffer** | Gratuit | Treemap temps réel pendant le scan | Dev abandonné, UI Win95 |
| **CCleaner Pro** | 20 €/an | **Nettoyage système et navigateurs très profond**, registry cleaner, startup manager | Scandales vie privée passés, gonflé de cross-sell |
| **Revo Uninstaller Pro** | 25 €/an | **Monitoring installs** pour détection rémanents parfaite | Focus uninstall uniquement, pas de vue globale disque |

### Matrice de couverture (✅ = fait, ⚠️ = partiel, ❌ = absent)

| Feature | DiskScout | WizTree | TreeSize Pro | WinDirStat | CCleaner | Revo |
|---|---|---|---|---|---|---|
| Scan MFT ultra-rapide | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Treemap | ❌ | ⚠️ | ✅ | ✅ | ❌ | ❌ |
| Sunburst | ❌ | ❌ | ⚠️ | ❌ | ❌ | ❌ |
| Tree view lazy | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Gros fichiers | ✅ | ✅ | ✅ | ⚠️ | ❌ | ❌ |
| Extensions | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Vieux fichiers | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Doublons nom+taille | ✅ | ❌ | ✅ | ❌ | ⚠️ | ❌ |
| Doublons hash | ❌ | ❌ | ✅ | ❌ | ⚠️ | ❌ |
| Rémanents AppData | ✅ | ❌ | ⚠️ | ❌ | ⚠️ | ✅ |
| Caches navigateurs | ✅ | ❌ | ❌ | ❌ | ✅ | ❌ |
| Caches dev | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Artefacts système | ✅ | ❌ | ⚠️ | ❌ | ✅ | ❌ |
| Cloud aware (OneDrive) | ✅ | ❌ | ⚠️ | ❌ | ❌ | ❌ |
| Dashboard santé | ✅ | ❌ | ⚠️ | ❌ | ⚠️ | ❌ |
| Recommandations prioritisées | ✅ | ❌ | ❌ | ❌ | ⚠️ | ❌ |
| Clic droit supprimer | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Undo / rollback | ❌ | ❌ | ⚠️ | ❌ | ❌ | ✅ |
| Scheduled scans | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ |
| CLI / scripting | ❌ | ⚠️ | ✅ | ❌ | ❌ | ❌ |
| Export PDF | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Export Excel formaté | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Remote scan UNC | ❌ | ⚠️ | ✅ | ⚠️ | ❌ | ❌ |
| NTFS compression hint | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| Reparse / junction viz | ⚠️ | ❌ | ✅ | ❌ | ❌ | ❌ |
| History / delta scans | ⚠️ backend | ❌ | ✅ | ❌ | ❌ | ❌ |

**Conclusion :** DiskScout a déjà une couverture **plus large** que chacun individuellement sur les axes analyse + rémanents + cloud + santé, mais **retarde fortement** sur 3 axes clés : **vitesse de scan (MFT)**, **visualisation (treemap/sunburst)**, **automation pro (CLI, scheduled, PDF)**.

---

## 3. Top 10 écarts critiques à combler pour dominer

1. **Scanner MFT NTFS** — lecture directe du Master File Table via `DeviceIoControl FSCTL_ENUM_USN_DATA` ou `FSCTL_QUERY_USN_JOURNAL`. Gain attendu : **10-30×** sur HDD, **3-5×** sur SSD. *C'est l'avantage compétitif unique de WizTree.* Sans ça, DiskScout reste « lent » dans l'esprit des utilisateurs.

2. **Treemap interactif** — panel custom WPF ou `LiveCharts2`/`SkiaSharp`. Algorithme squarified (ratio d'aspect proche de 1). Drill-down au clic, zoom, tooltip, coloriage par type ou par âge. *La killer feature visuelle.*

3. **Doublons par hash** — SHA-256 ou mieux **xxHash3** (beaucoup plus rapide). Stratégie : bucket par taille → partial hash (premiers 64 Ko) → full hash si collision. Affichage : groupes triés par octets gaspillés.

4. **CLI complète** — `DiskScout scan C:\ --output scan.json`, `DiskScout diff scan1.json scan2.json`, `DiskScout report scan.json --format pdf`. Ouvre la porte aux scripts d'admin, CI/CD, enterprise.

5. **Scheduled scans** — intégration Windows Task Scheduler. « Scan hebdo dimanche 3h, email résultat ». Seule TreeSize Pro le fait proprement.

6. **Export PDF/Excel pro** — `QuestPDF` (gratuit) pour PDF, `ClosedXML` pour Excel. Rapports mis en forme, charts inclus, page de garde. Le type d'export que DSI et compta demandent.

7. **Undo/Quarantine** — avant vraie suppression, option « déplacer vers `%LocalAppData%\DiskScout\quarantine\session_YYYYMMDD\` pendant 30 jours ». Un clic pour restaurer. *Filet de sécurité que personne ne fait bien.*

8. **Compression suggestion** — détecter les gros fichiers compressibles (logs, uncompressed binaries, texte brut) et proposer NTFS compression via `compact.exe /c`. Gain typique : 30-50 % sur certains dossiers.

9. **USN journal pour rescan incrémental** — ne plus rescanner tout le disque. Écouter le journal USN depuis le dernier scan, appliquer les deltas. Les rescans passent de 3 min à < 2 s.

10. **File safety score** — ne pas proposer la suppression si : chemin dans `.git`, fichier signé Microsoft/Adobe/Autodesk, fichier dans un projet avec `*.sln`/`package.json` récemment modifié. Evite 90 % des regrets utilisateurs.

---

## 4. Feuille de route priorisée

### Phase A — Parité visuelle + robustesse (1-2 semaines)

| # | Feature | Effort | Valeur |
|---|---|---|---|
| A1 | **Treemap squarified** interactif avec drill-down, coloriage type/âge | L | ⭐⭐⭐⭐⭐ |
| A2 | **Sunburst chart** hiérarchique (DaisyDisk-like) en complément | M | ⭐⭐⭐⭐ |
| A3 | **Doublons xxHash3** en 3 passes (taille → partial → full) | M | ⭐⭐⭐⭐⭐ |
| A4 | **Quarantine avec rétention 30j** + onglet « Corbeille DiskScout » | M | ⭐⭐⭐⭐ |
| A5 | **ETA + débit** sur la progress bar | S | ⭐⭐⭐ |
| A6 | **Pause/Reprise** du scan | M | ⭐⭐⭐ |
| A7 | **Recherche globale** (Ctrl+F) dans l'arbo et les tableaux | S | ⭐⭐⭐⭐ |
| A8 | **Multi-select + opérations bulk** avec confirmation agrégée | M | ⭐⭐⭐⭐ |

### Phase B — Vitesse et automation (2-4 semaines)

| # | Feature | Effort | Valeur |
|---|---|---|---|
| B1 | **Scanner MFT** via `DeviceIoControl` + `USN_RECORD_V3` parse | XL | ⭐⭐⭐⭐⭐ |
| B2 | **Fallback automatique** FindFirstFileEx pour non-NTFS (FAT, exFAT, ReFS) | S | ⭐⭐⭐⭐ |
| B3 | **Rescan incrémental USN journal** entre 2 scans | L | ⭐⭐⭐⭐⭐ |
| B4 | **CLI complète** (scan, diff, report, cleanup dry-run) | L | ⭐⭐⭐⭐ |
| B5 | **Scheduled scans** via Task Scheduler + email SMTP | L | ⭐⭐⭐ |
| B6 | **Export PDF** via QuestPDF avec page de garde + charts | M | ⭐⭐⭐⭐ |
| B7 | **Export Excel mis en forme** via ClosedXML | M | ⭐⭐⭐ |
| B8 | **History multi-scans** avec graphique timeline de croissance | M | ⭐⭐⭐⭐ |

### Phase C — Intelligence et sécurité (1-2 mois)

| # | Feature | Effort | Valeur |
|---|---|---|---|
| C1 | **File safety score** (git / signé / projet actif) | M | ⭐⭐⭐⭐⭐ |
| C2 | **Détection de l'application propriétaire** d'un dossier AppData (pattern lookup) | M | ⭐⭐⭐⭐ |
| C3 | **Compression NTFS** — détection + one-click `compact.exe /c` | M | ⭐⭐⭐⭐ |
| C4 | **Near-duplicates images** via perceptual hash (pHash) | L | ⭐⭐⭐⭐ |
| C5 | **Steam/Epic/GOG** games avec date dernière session (API Steam) | M | ⭐⭐⭐ |
| C6 | **WSL disks** détection et analyse | M | ⭐⭐⭐ |
| C7 | **Google Drive / Dropbox / iCloud** détection placeholders | M | ⭐⭐⭐⭐ |
| C8 | **Broken shortcuts** (.lnk pointant vers rien) | S | ⭐⭐⭐ |
| C9 | **Empty folders** globalement (pas juste Program Files) | S | ⭐⭐⭐ |
| C10 | **Alternate Data Streams** listing | S | ⭐⭐ |

### Phase D — Enterprise et différenciation (2-3 mois)

| # | Feature | Effort | Valeur |
|---|---|---|---|
| D1 | **Multi-machine dashboard** (scan N postes, dashboard agrégé local) | XL | ⭐⭐⭐ |
| D2 | **Remote scan UNC** `\\server\share` avec creds | L | ⭐⭐⭐ |
| D3 | **Explorateur integration** (shell ext `HKLM\...\shellex\ContextMenuHandlers`) | M | ⭐⭐⭐⭐ |
| D4 | **Config profiles** exportables (whitelist, seuils, plan nettoyage) | S | ⭐⭐⭐ |
| D5 | **Audit log export** signé, pour compliance | M | ⭐⭐⭐ |
| D6 | **REST API locale** (http://localhost:9999) pour scripting | L | ⭐⭐ |
| D7 | **MSI installer** en plus du portable pour déploiement GPO | M | ⭐⭐⭐ |
| D8 | **Theme clair** + HiDPI parfait + accessibilité | M | ⭐⭐⭐ |
| D9 | **Installation signée Authenticode** pour réduire SmartScreen | M | ⭐⭐⭐⭐ |
| D10 | **Onboarding tutoriel** première ouverture | S | ⭐⭐⭐ |

---

## 5. Évolutions d'architecture requises

### Stack (ajouts NuGet)

| Package | Phase | Usage |
|---|---|---|
| `System.IO.Hashing` | A3 | xxHash3 pour dédup rapide |
| `QuestPDF` | B6 | Génération PDF |
| `ClosedXML` | B7 | Excel mis en forme |
| `SkiaSharp` ou `LiveChartsCore` | A1, A2 | Treemap + sunburst performants |
| `Microsoft.Toolkit.Uwp.Notifications` | B5 | Toast Windows post-scheduled |
| `System.CommandLine` | B4 | Parsing CLI |
| `CoreCompat.System.Drawing` | C4 | Perceptual hash images |

### Refactos structurels

1. **Séparer le domaine du rendu WPF** — créer `DiskScout.Core` (netstandard2.0 ou net8.0) contenant Models/Services/Helpers. `DiskScout.App` (WPF) devient la couche présentation. `DiskScout.Cli` réutilise `Core`. Permet CLI, tests unitaires plus solides, et éventuellement une UI web future.

2. **Persistance événementielle** — au lieu d'un unique snapshot JSON par scan, stocker chaque scan dans SQLite local avec index sur chemin + taille + date. Permet : requêtes rapides, history multi-scans, delta arbitraire.

3. **Service de scan pluggable** — interface `IFileSystemEnumerator` avec 2 impls : `FindFirstFileEnumerator` (universel) et `MftEnumerator` (NTFS only, ultra-rapide). Choix auto selon FS.

4. **Event-driven progress** — passer d'un `IProgress<ScanProgress>` throttlé à un bus d'événements (`IAsyncEnumerable<ScanEvent>`) pour que la treemap temps-réel, l'ETA et les logs soient des abonnés distincts.

5. **Plugins** — définir un contrat `ICleanupHeuristic` pour que les détections (rémanents, caches, artefacts) soient enregistrables dynamiquement. Ouvre la porte à des règles externes YAML/JSON.

6. **Code signing** — intégrer la signature Authenticode à la pipeline de publish. Sans ça, SmartScreen bloque tous les nouveaux utilisateurs et freine l'adoption.

7. **Telemetry opt-in** (éthique) — usage anonyme (temps de scan, types de disques, tailles). Sans ça, les décisions produit futures seront aveugles.

---

## 6. Positionnement marché

### Proposition de valeur différenciatrice

> **« DiskScout : le seul outil qui combine vitesse WizTree, visualisation WinDirStat, profondeur CCleaner et intelligence cloud — sans vous vendre ce que vous savez déjà faire. »**

### Pricing / Distribution

Trois scénarios réalistes :

**A. Open source + donations**
- Stratégie GitHub-first, MIT/Apache
- Communauté devient le canal de distribution
- Donations via GitHub Sponsors
- Pas de revenu direct mais réputation + CV + possibilité pivot pro plus tard

**B. Freemium**
- **Gratuit** : scan, arbo, santé, rémanents, cleanup de base
- **Pro (30 €/an)** : MFT scan, treemap, hash dedup, scheduled, PDF/Excel, CLI, undo, multi-machine
- Positionnement en-dessous de TreeSize Pro (60 €) mais au-dessus de WizTree (gratuit)

**C. Bundle entreprise**
- Licence site 200-500 € pour déploiement GPO
- Support, rapports compliance, intégration SCCM
- Ciblage DSI / MSP

→ Recommandation : commencer **A** pendant 3-6 mois pour construire l'audience, puis basculer **B** avec Pro Annuel quand les features Phase B/C/D sont disponibles. La base utilisateurs gratuite devient le canal d'acquisition payant.

### Canaux d'acquisition

1. **GitHub release + README soigné** avec GIF/captures — les outils disque sont massivement découverts sur GitHub
2. **Article blog technique** sur le MFT scanner (Hacker News / Reddit r/sysadmin) — positionnement expertise
3. **Vidéo YouTube** comparative « J'ai testé 6 outils de nettoyage disque en 2026 » — canal dominant pour cette audience
4. **Forum r/Windows10, r/sysadmin, r/DataHoarder** — user base mûre et gros disques
5. **Comparison pages** (alternativeto.net, softpedia) — listing indispensable

### Positionnement dans 12 mois (si roadmap suivie)

| Attribut | WizTree | TreeSize Pro | CCleaner | DiskScout post-roadmap |
|---|---|---|---|---|
| Scan speed | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| Visualisation | ⭐⭐ | ⭐⭐⭐⭐ | ⭐ | ⭐⭐⭐⭐⭐ |
| Cleanup depth | ⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Cloud awareness | ⭐ | ⭐⭐ | ⭐ | ⭐⭐⭐⭐⭐ |
| Smart suggestions | ⭐ | ⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| Safety | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Prix / valeur | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |

---

## 7. Décisions à prendre rapidement

Avant d'attaquer la Phase A, trancher sur **5 points stratégiques** :

1. **Open source ou propriétaire ?** Impacte la licence, le repo, la stratégie commerciale. Recommandation : open-source noyau + closed-source Pro.

2. **Garder .NET 8 ou migrer .NET 10 LTS maintenant ?** .NET 8 EOL 2026-11-10. Mieux vaut migrer avant de stabiliser.

3. **Refactor `Core` / `App` / `Cli` avant ou après Phase A ?** Recommandation : **avant** — les évolutions Phase B sont ingérables sans cette séparation.

4. **SQLite vs JSON pour la persistance ?** Recommandation : **SQLite** dès Phase A. JSON ne tiendra pas avec history multi-scans.

5. **Nom de marque et domaine** — `DiskScout` est bon. Vérifier `diskscout.io`, `diskscout.app`. Logo déjà fait.

---

## 8. Métriques de succès (12 mois)

| KPI | Cible |
|---|---|
| Scan 500 Go SSD | < 15 s (vs 3 min aujourd'hui) |
| Scan 4 To NTFS | < 60 s |
| Installations actives | 10 000 |
| GitHub stars | 1 000 |
| Taux de conversion Pro | 3 % |
| NPS | > 40 |

---

*Ce document est la boussole stratégique de DiskScout. À réviser tous les trimestres.*
