# DiskScout — Guide d'utilisation complet

**Version :** 0.1.0 — avril 2026

---

## 1. Démarrage

1. **Lancement** : double-clique sur `DiskScout.exe`. Windows demande l'élévation (UAC) — accepte, l'app a besoin des droits admin pour lire `Program Files`, le registre `HKLM`, `AppData` d'autres utilisateurs et les `$Recycle.Bin` de chaque disque.
2. **Titre de la fenêtre** : `DiskScout [Admin]` confirme l'élévation.
3. **Logs** : chaque session écrit dans `%LocalAppData%\DiskScout\diskscout.log` (rotation 5 Mo / 5 fichiers).

---

## 2. Barre d'outils (en haut de la fenêtre)

| Zone | Utilité |
|---|---|
| **Logo + titre** | Identifie la session et confirme `[Admin]` |
| **Sélecteur de disques** | Coche/décoche les disques fixes à analyser. Chaque case indique label + espace libre + format (NTFS/FAT32…) |
| **Scanner** | Lance un scan complet des disques cochés |
| **Scanner dossier…** | Ouvre un sélecteur de dossier pour analyser **uniquement** un sous-arbre (Downloads, Bureau, un projet) |
| **Annuler** | Interrompt proprement le scan en cours (libération sous 2 s des handles P/Invoke) |
| **Exporter PDF** | Génère un rapport PDF A4 mis en forme du dernier scan. Activé une fois un scan terminé |

**Zone de progression (juste en-dessous)** :
- Ligne 1 : état (Scan en cours… / Terminé. / Annulé…) + barre animée + pourcentage
- Ligne 2 : chemin courant + **débit** (fichiers/s, Mo/s) + **temps écoulé**
- Tout est live, throttlé à 10 Hz pour ne pas saturer le thread UI

**Barre de statut (bas de fenêtre)** : résumé post-scan, ex. *« Scan terminé en 02:31 — 1 255 376 nœuds, 163 programmes, 2 758 rémanents »*.

---

## 3. Les 11 onglets en détail

### 📊 Santé (landing)

Vue dashboard après chaque scan, tout le résumé en un écran.

- **Badge Grade** (A+, A, B, C, D) + score **0-100** coloré (vert→rouge)
- **Phrase de résumé** contextuelle + 3 KPIs (disques analysés / occupé total / récupérable)
- **Tuiles Disques** (1 par disque scanné) : % utilisé en gros, barre colorée selon seuil (vert >20% libre, jaune <20, orange <10, rouge <5), pill de statut
- **Actions recommandées** (bandeau droit) : top 4 priorités avec bordure gauche colorée + octets récupérables. Pointeur « → Onglet X »
- **4 cartes métriques** : Rémanents / Nettoyage / Doublons / Vieux fichiers, bordure basse colorée selon seuil
- **Top 8 extensions par octets** + **Top 6 dossiers racine** avec barres proportionnelles

*Quand l'utiliser :* première vue après un scan, pour décider où agir en priorité.

---

### 💿 Programmes

Liste des programmes installés lus depuis **4 vues registre** : `HKLM` Reg64 + Reg32 (WOW6432Node) + `HKCU` Reg64 + Reg32.

- Colonnes : Nom (DisplayName), Éditeur, Version, Installé (date), Taille (recalculée à partir du scan d'InstallLocation), Chemin, Hive
- Tri par toutes les colonnes (cliquer l'en-tête)
- **Barre de recherche** en haut — filtre sur nom ou éditeur en temps réel

*Quand l'utiliser :* repérer les programmes gourmands, identifier les installs obsolètes avant désinstallation via Panneau de configuration.

---

### 🔍 Rémanents (catégories historiques)

Fichiers résiduels de programmes désinstallés, 4 heuristiques **de base** :

| Catégorie | Détection |
|---|---|
| **AppData orphelins** | Dossiers dans `%LocalAppData%`, `%AppData%`, `%ProgramData%` dont le nom ne matche aucun programme installé (fuzzy match 2 étapes : exact/préfixe puis token Jaccard) |
| **Program Files vides** | Sous-dossiers de `Program Files` / `Program Files (x86)` < 1 Mo ou contenant uniquement `.log`/`.txt` |
| **Temp anciens** | Fichiers dans `%TEMP%` / `%LocalAppData%\Temp` non modifiés depuis > 30 j |
| **Patches MSI orphelins** | `.msp` dans `C:\Windows\Installer` non référencés par un programme actif |

Groupés par catégorie, triés par taille décroissante. **Clic droit** → Copier le chemin / Supprimer (corbeille ou définitif).

*Quand l'utiliser :* après un ménage de programmes, pour supprimer les traces.

---

### 🧹 Nettoyage (catégories avancées)

Même UX que Rémanents, 3 catégories supplémentaires :

| Catégorie | Détection |
|---|---|
| **Artefacts système** | `hiberfil.sys`, `pagefile.sys`, `swapfile.sys` à la racine ; `C:\Windows.old` ; `$Recycle.Bin` ; cache Windows Update (`SoftwareDistribution\Download`) ; Prefetch ; CrashDumps / Minidump / LiveKernelReports ; Delivery Optimization cache. Seuil 50 Mo |
| **Caches de développement** | Dossiers nommés `node_modules`, `packages` (.nuget), `__pycache__`, `.pytest_cache`, `.mypy_cache`, `.gradle`, `.cargo`, `venv`, `.venv`, `target`, `.tox`, `.next`, `.turbo`, `dist`, `build`. Seuil 50 Mo. Suppression des descendants (pas de double-comptage) |
| **Caches de navigateurs** | Dossiers `Cache`/`cache2`/`Code Cache`/`GPUCache`/`Service Worker`/etc. sous Chrome / Edge / Brave / Opera / Vivaldi / Firefox. Seuil 20 Mo |

**Dossiers vides** (depth ≥ 2, hors Program Files) et **Raccourcis cassés** (`.lnk` dont la cible n'existe plus, résolue via WScript.Shell) apparaissent ici aussi.

*Quand l'utiliser :* nettoyage ponctuel gros volumes ; les caches se régénèrent automatiquement, sans risque.

---

### 🌳 Arborescence (Tree view)

TreeView hiérarchique virtualisé (lazy-load par nœud, max 500 enfants par niveau).

**Barre d'outils de l'onglet** :
- **Filtre taille mini (Go)** — ex. 5 masque les dossiers < 5 Go à tous les niveaux
- **Analyser les types** — lance l'analyse doc-type bottom-up (algo O(N))
- **Réinitialiser l'analyse** — repasse en mode taille (single color)
- **Heatmap d'âge** — checkbox. Colorise la barre selon le last-modified : vert < 30 j, jaune < 180 j, orange < 365 j, rouge < 3 ans, rouge foncé au-delà

**Légende d'analyse** (si active) : 11 types colorés avec leur % global — PDF, XLSX, RVT, TXT, DLL, SYS, EXE, Images, Vidéos, Archives, Autres.

**Chaque nœud** affiche :
- Nom (avec suffixe `[OneDrive/SharePoint]` ou `[Reparse]` ou `[supprimé]` si applicable)
- Taille formatée
- **Barre proportionnelle** (2/3 de la largeur de ligne) : fill selon SharePercent, avec 11 segments doc-type si analyse active

**Clic droit** → Copier le chemin / Supprimer (corbeille / définitif). Les volumes racines ne peuvent pas être supprimés.

*Quand l'utiliser :* exploration manuelle, drill-down dans les gros dossiers.

---

### 🗺️ Carte (Treemap — flagship visuel)

Vue treemap squarified (algorithme Bruls/Huijsmans/van Wijk) où chaque rectangle est proportionnel à sa taille.

**Barre d'outils** :
- **↑ Remonter** — revenir au niveau parent
- **Boutons de coloriage** : Profondeur (palette cycle) / Âge (vert→rouge) / Type (par catégorie doc)
- **Chemin courant** affiché
- **Breadcrumb strip** — cliquable pour revenir à n'importe quel niveau

**Interaction** :
- **Clic gauche** sur un rectangle → drill-down dans ce dossier
- **Clic droit** → menu Copier / Supprimer
- **Survol** → tooltip avec nom complet + chemin + taille

*Quand l'utiliser :* comprendre en un coup d'œil où va l'espace, trouver les gros blocs cachés.

---

### 📁 Gros fichiers

Top N fichiers les plus lourds (défaut 200, paramétrable 10-2000).

- Colonnes : Nom, Taille, Ext, Modifié, Chemin
- Tri par toutes les colonnes, recherche libre (nom/chemin/ext)
- Clic droit → Copier / Supprimer

*Quand l'utiliser :* repérer instantanément les ISO, VHDX, MP4, archives oubliés.

---

### 🏷️ Extensions

Classement de toutes les extensions par octets cumulés.

- Colonnes : Extension, Fichiers, Taille, % disque
- Tri par toutes les colonnes

*Quand l'utiliser :* comprendre quel *type* de fichier mange le disque (ex : « .rvt = 34% de mon disque »).

---

### 🔁 Doublons

Groupes de fichiers identiques par **nom + taille**, triés par octets gaspillés.

**Workflow en 2 passes** :
1. **Passe 1 (automatique)** — groupage par `(nom, taille)`. Rapide mais peut avoir des faux positifs (deux fichiers `config.json` de 2 Ko dans des projets différents).
2. **Passe 2 (manuelle)** — bouton **Vérifier par hash (xxHash3)**. Hash partiel 64 Ko puis hash complet en cas de collision. Élimine les faux positifs. Ligne de statut indique le compte vrai vs faux.

- Filtre **taille mini (Mo)** pour éviter le bruit (défaut 1 Mo)
- Groupés par `{nom} — {taille} × N copies → X Mo gaspillés`
- Clic droit → Copier / Supprimer

*Quand l'utiliser :* libérer de la place sans rien perdre ; garde 1 copie, supprime les autres.

---

### ⏳ Vieux fichiers

Fichiers non modifiés depuis N jours, groupés par extension.

**Filtres** :
- **Âge mini (jours)** — défaut 365
- **Taille mini (Mo)** — défaut 10

- Colonnes : Nom, Âge (jours/années), Modifié, Taille, Chemin
- Groupé par extension, tri par taille
- Clic droit → Copier / Supprimer

*Quand l'utiliser :* archiver sur disque externe les gros fichiers dormants.

---

### 🛡️ Quarantaine

Zone tampon pour les suppressions — **30 jours de rétention avant purge**.

**Workflow** :
1. Les suppressions via DiskScout vont **par défaut dans la corbeille Windows** (réversible natif).
2. Future évolution : le système de quarantaine se déclenchera sur un profil « suppression sûre » qui déplace vers `%LocalAppData%\DiskScout\quarantine\session_YYYYMMDD_HHmmss\` en préservant la structure de chemin.
3. Chaque session a son **manifest.json** listant les fichiers + leur chemin d'origine.
4. **À l'ouverture de l'app**, les sessions > 30 jours sont purgées automatiquement en arrière-plan.

**Dans l'onglet** :
- Liste groupée par session, avec « Expire dans N j » ou « Expirée (à purger) »
- **Rafraîchir** — recharge le contenu (utile après restauration manuelle)
- **Clic droit → Restaurer** — replace le fichier à son emplacement d'origine
- **Purger les expirées** — supprime définitivement les sessions > 30 j après confirmation

*Quand l'utiliser :* récupérer un fichier supprimé par erreur pendant la fenêtre de 30 j.

---

## 4. Système de suppression (règle de sécurité critique)

Toutes les suppressions passent par un **dialog en 2 étapes** :

1. **Premier dialog** : résumé (chemin + taille) + 3 boutons
   - **Oui** → Corbeille Windows (réversible, **recommandé**)
   - **Non** → Suppression définitive (irréversible)
   - **Annuler** → ne rien faire
2. **Si Non choisi** : **second dialog** stop avec le résumé et confirmation finale. **Deux clics minimum** pour une suppression définitive.

Tout est **loggué** dans `diskscout.log` (succès + échecs) pour audit trail complet.

**Règles de sécurité** :
- Les volumes racines ne peuvent pas être supprimés
- Un futur `FileSafety.Score` (déjà codé) flaggera à 0 les chemins OS-critiques (System32, WinSxS) et à 10 les dossiers sous `.git` / `*.sln` / `package.json`

---

## 5. Scans multiples et export

- **Persistance** : chaque scan est auto-sauvegardé en JSON dans `%LocalAppData%\DiskScout\scans\scan_YYYYMMDD_HHmmss.json` avec `schemaVersion: 1`.
- **Format plat** (liste de nœuds avec `ParentId`) — évite les stack overflow sur arbres à 1M+ nœuds.
- **Export CSV / HTML** déjà dispo backend (pas de bouton UI en v0.1, mais `CsvHtmlExporter` est wiré).
- **Export PDF** : bouton **Exporter PDF** dans le header → SaveFileDialog → génération A4 avec badge santé, synthèse, top 20 fichiers, top 10 programmes.

---

## 6. Fichiers et dossiers de l'app

| Chemin | Contenu |
|---|---|
| `%LocalAppData%\DiskScout\diskscout.log` | Log Serilog rotatif (5 Mo × 5) |
| `%LocalAppData%\DiskScout\scans\` | JSON de chaque scan |
| `%LocalAppData%\DiskScout\quarantine\` | Sessions quarantaine (30 j) |

L'app elle-même est un **single-file portable** : déplace `DiskScout.exe`, relance, les données suivent. Désinstaller = supprimer l'exe et le dossier `%LocalAppData%\DiskScout`.

---

## 7. Scénarios types

### Scénario 1 — « Mon disque C: est plein »
1. Coche C:, clique **Scanner**
2. Onglet **Santé** → regarde Grade + Actions recommandées
3. Onglet **Nettoyage** → supprime artefacts système + caches navigateurs (corbeille)
4. Onglet **Gros fichiers** → repère ISO/VHDX oubliés, supprime
5. Onglet **Doublons** → **Vérifier par hash** puis supprime 1 copie sur 2 des vrais doublons
6. Vérifie la libération dans la corbeille Windows ou sous `%LocalAppData%\DiskScout\quarantine\` selon le mode choisi

### Scénario 2 — « Je veux voir ce qui prend de la place »
1. Scanner
2. Onglet **Carte** → mode de coloriage **Type** → drill-down dans les plus gros rectangles
3. Onglet **Extensions** → voir quel type domine
4. Onglet **Arborescence** → cocher **Analyser les types** pour ventilation par dossier

### Scénario 3 — « Rapport pour ma DSI / mon client »
1. Scanner le disque concerné
2. Clique **Exporter PDF** dans le header
3. Choisis l'emplacement → le PDF contient grade de santé, métriques clés, top 20 fichiers, top 10 programmes, prêt à envoyer

### Scénario 4 — « Rapide check avant réunion »
1. **Scanner dossier…** → choisis `C:\Users\<toi>\Downloads`
2. Onglet **Vieux fichiers** → filtre âge 90 j, taille 100 Mo → tout ce qui peut partir
3. Clic droit → Supprimer en masse (corbeille)

### Scénario 5 — « J'ai supprimé par erreur »
1. Ouvre **Quarantaine** (si le fichier est passé par là — sinon va direct dans la corbeille Windows)
2. Repère-le dans sa session
3. Clic droit → **Restaurer** → il retourne à son emplacement d'origine

---

## 8. Raccourcis clavier

- `Ctrl+Clic` sur onglet → (futur : multi-sélection — en roadmap)
- `F5` → (futur : rescan)
- `Delete` → supprime la ligne sélectionnée (contextuel onglet)
- `Ctrl+C` dans un tableau → copie le chemin

*Note : plusieurs raccourcis sont en roadmap Phase B/C, pas tous câblés en v0.1.*

---

## 9. Limites connues (v0.1.0)

- **Vitesse de scan** : `FindFirstFileEx` — ~3 min / 500 Go SSD. La migration vers lecture **MFT directe** (Phase B roadmap) divisera par 10-30.
- **Multi-select** : pas encore dans l'UI (Phase A post-audit).
- **Scheduled scans** : pas dans v0.1.
- **CLI** : pas dans v0.1 (nécessite refactor Core/App/Cli prévu).
- **Export Excel formaté** : backend à faire (Phase B).
- **Détection images near-duplicate (pHash)** : Phase C.

Roadmap complète dans `.planning/MARKET-AUDIT.md`.

---

*DiskScout — v0.1.0, avril 2026.*
