# Corpus brut — audit utilisateur de 365 items sur C:\ProgramData

> Source : audit manuel fourni par l'utilisateur dans le prompt initial de Phase 10 (2026-04-27).
> Machine de référence : HP corporate (NinjaRMM + Zscaler + Matrix42 + Empirum + Bitdefender + Splashtop + suite Autodesk + Office 365 + Microsoft Teams + Claude Desktop + Python Manager + WhatsApp UWP).
> **Usage par plan 10-05** : convertir ce fichier en `tests/DiskScout.Tests/Fixtures/programdata_corpus_365.json` (un objet par item avec `path`, `verdict`, `recommendedAction`, `reason`).

## Mapping verdict humain → enum RiskLevel + RecommendedAction

| Symbole audit | RiskLevel | RecommendedAction |
|---|---|---|
| 🟢 AUCUN | Aucun | Supprimer |
| 🟡 FAIBLE | Faible | CorbeilleOk |
| 🟠 MOYEN | Moyen | VerifierAvant |
| 🔴 ÉLEVÉ | Eleve | NePasToucher |
| ⛔ CRITIQUE | Critique | NePasToucher |

`GARDER` = explicitement conservé (mappé sur `Garder`).

---

## Items 1-50

[1] C:\ProgramData\KUBUS\BcfManager — 🟠 MOYEN — Données de KUBUS BCF Manager (plugin BIM Revit) — 6 Go, probablement des modèles BCF actifs. Le nom registre est « BCF Managers 6.5 - Revit 2021 - 2024 » d'où le faux positif. Action : VÉRIFIER AVANT.
[2] C:\ProgramData\Microsoft\VisualStudio — ⛔ CRITIQUE — Dossier de configuration de Visual Studio + Build Tools + telemetry — 6 Go. Action : NE PAS TOUCHER.
[3] C:\ProgramData\Autodesk\RVT 2025 — 🔴 ÉLEVÉ — Données partagées de Revit 2025 (familles, templates, content libraries) — 2,6 Go. Action : NE PAS TOUCHER.
[4] C:\ProgramData\Package Cache — ⛔ CRITIQUE — Cache global Windows Installer/Burn. Action : NE PAS TOUCHER.
[5] C:\ProgramData\Autodesk\Revit Steel Connections 2025 — 🔴 ÉLEVÉ — Composant officiel Autodesk pour Revit 2025. Action : NE PAS TOUCHER.
[6] C:\ProgramData\DiRoots.One\updates — 🟠 MOYEN — Cache de mises à jour DiRoots.One. Action : VÉRIFIER AVANT.
[7] C:\ProgramData\Microsoft\Windows Defender — ⛔ CRITIQUE — Définitions et données de l'antivirus Windows Defender. Action : NE PAS TOUCHER.
[8] C:\ProgramData\Autodesk\Geospatial Coordinate Systems 15.01 — 🔴 ÉLEVÉ — Bibliothèque de systèmes de coordonnées partagée. Action : NE PAS TOUCHER.
[9] C:\ProgramData\Package Cache\{5cae2bd7-...}v2.152.1279.0 — 🟠 MOYEN — Cache MSI/EXE installeur. Action : VÉRIFIER AVANT.
[10] C:\ProgramData\Autodesk\Uninstallers — 🔴 ÉLEVÉ — Cache des désinstalleurs Autodesk. Action : NE PAS TOUCHER.
[11] C:\ProgramData\NVIDIA\NGX — 🔴 ÉLEVÉ — Modèles IA NVIDIA NGX (DLSS, RTX). Action : NE PAS TOUCHER.
[12] C:\ProgramData\Package Cache\{F05407CE-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK pour Windows 11 24H2. Action : NE PAS TOUCHER.
[13] C:\ProgramData\Autodesk\ODIS — 🔴 ÉLEVÉ — Autodesk Desktop Connector / Installer Service. Action : NE PAS TOUCHER.
[14] C:\Users\tanguy.delrieu\AppData\LocalLow — ⛔ CRITIQUE — Dossier Windows standard pour applications low-integrity. Action : NE PAS TOUCHER.
[15] C:\ProgramData\Autodesk\AECGD — 🔴 ÉLEVÉ — AEC Geographic Data. Action : NE PAS TOUCHER.
[16] C:\ProgramData\Matrix42\Logs — 🔴 ÉLEVÉ — Logs Matrix42 (gestion endpoint corporate). Action : NE PAS TOUCHER.
[17] C:\ProgramData\Zscaler\log-0028E037B8101BD2C5459365BF23F67BDFF9B9E8 — 🔴 ÉLEVÉ — Logs Zscaler corporate. Action : NE PAS TOUCHER.
[18] C:\ProgramData\Autodesk\Structural — 🔴 ÉLEVÉ — Bibliothèque structurelle Autodesk. Action : NE PAS TOUCHER.
[19] C:\ProgramData\NinjaRMMAgent\logs — 🔴 ÉLEVÉ — Logs NinjaOne RMM corporate. Action : NE PAS TOUCHER.
[20] C:\ProgramData\Autodesk\Revit Interoperability 2025 — 🔴 ÉLEVÉ — Composant d'interopérabilité Revit 2025. Action : NE PAS TOUCHER.
[21] C:\ProgramData\Package Cache\{7D4D186C-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[22] C:\ProgramData\Package Cache\{BB146BF9-...}v6.17.21301 — 🟠 MOYEN — Cache MSI Autodesk Identity Manager / AdSSO. Action : VÉRIFIER AVANT.
[23] C:\ProgramData\HP\HP Touchpoint Analytics Client — 🟠 MOYEN — Service télémétrie HP. Action : VÉRIFIER AVANT.
[24] C:\ProgramData\Package Cache\{4C150435-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[25] C:\ProgramData\Package Cache\{CA4D7166-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[26] C:\ProgramData\Autodesk\ApplicationPlugins — 🔴 ÉLEVÉ — Plugins Autodesk partagés. Action : NE PAS TOUCHER.
[27] C:\ProgramData\Microsoft\ClickToRun — ⛔ CRITIQUE — Moteur d'installation Click-to-Run de Microsoft Office 365. Action : NE PAS TOUCHER.
[28] C:\ProgramData\Autodesk\AdskLicensingService — ⛔ CRITIQUE — Service de licences Autodesk. Action : NE PAS TOUCHER.
[29] C:\ProgramData\_Empirum — 🔴 ÉLEVÉ — Données Matrix42 Empirum (déploiement logiciel corporate). Action : NE PAS TOUCHER.
[30] C:\ProgramData\NinjaRMMAgent\download — 🔴 ÉLEVÉ — Cache de téléchargement NinjaOne corporate. Action : NE PAS TOUCHER.
[31] C:\ProgramData\NinjaRMMAgent\NinjaWPM — 🔴 ÉLEVÉ — Module NinjaOne Windows Performance Monitor. Action : NE PAS TOUCHER.
[32] C:\ProgramData\NinjaRMMAgent\components — 🔴 ÉLEVÉ — Composants binaires de l'agent NinjaOne. Action : NE PAS TOUCHER.
[33] C:\ProgramData\Package Cache\{B7BC83B0-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[34] C:\ProgramData\Package Cache\{2C37C218-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[35] C:\ProgramData\Package Cache\{2CAA22A5-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[36] C:\ProgramData\Package Cache\{29177194-...}v64.92.45458 — 🟠 MOYEN — Cache MSI Visual C++ Redistributable x64. Action : VÉRIFIER AVANT.
[37] C:\ProgramData\Package Cache\{9C751D0A-...}v80.8.45513 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[38] C:\ProgramData\Package Cache\{61D4736B-...}v48.144.23186 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[39] C:\ProgramData\Package Cache\{09C20E7A-...}v80.8.45513 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[40] C:\ProgramData\Package Cache\{3A6767BB-...}v64.44.23253 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[41] C:\ProgramData\Package Cache\{39884818-...}v64.92.45415 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[42] C:\ProgramData\Package Cache\{C912E33F-...}v48.144.23141 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[43] C:\ProgramData\Package Cache\{9A00C541-...}v48.144.23186 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[44] C:\ProgramData\Package Cache\C483F66C48BA83E99C764D957729789317B09C6B — 🟠 MOYEN — Cache Burn (bootstrapper). Action : VÉRIFIER AVANT.
[45] C:\ProgramData\Package Cache\{9E437768-...}v64.44.23191 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[46] C:\ProgramData\Package Cache\{89C09E22-...}v48.144.23141 — 🟠 MOYEN — Cache MSI VC++ Redistributable. Action : VÉRIFIER AVANT.
[47] C:\ProgramData\Package Cache\{B5534577-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[48] C:\ProgramData\Package Cache\{12C6754C-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[49] C:\ProgramData\Autodesk\CDX — 🔴 ÉLEVÉ — Common Data eXchange Autodesk. Action : NE PAS TOUCHER.
[50] C:\ProgramData\USOShared — ⛔ CRITIQUE — Update Session Orchestrator. Action : NE PAS TOUCHER.

## Items 51-100

[51] C:\ProgramData\USOShared\Logs — ⛔ CRITIQUE — Logs USO de Windows Update. Action : NE PAS TOUCHER.
[52] C:\ProgramData\Package Cache\{9EE92E02-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[53] C:\ProgramData\Package Cache\{099E916C-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[54] C:\ProgramData\Package Cache\B87C38D093872D7BE7E191F01107B39C87888A5A — 🟠 MOYEN — Cache Burn. Action : VÉRIFIER AVANT.
[55] C:\ProgramData\Package Cache\AFA5BADCE64EE67290ADD24E0DC3D8210954AC6C — 🟠 MOYEN — Cache Burn. Action : VÉRIFIER AVANT.
[56] C:\ProgramData\Package Cache\C9B5B7969E499A4FD9E580EF4187322778E1936A — 🟠 MOYEN — Cache Burn. Action : VÉRIFIER AVANT.
[57] C:\ProgramData\Microsoft\EdgeUpdate — ⛔ CRITIQUE — Service de mise à jour Microsoft Edge. Action : NE PAS TOUCHER.
[58] C:\ProgramData\Package Cache\{595B374E-...}v1.0.14.0 — 🟠 MOYEN — Cache MSI. Action : VÉRIFIER AVANT.
[59] C:\ProgramData\Package Cache\{C4175120-...}v1.0.20.0 — 🟠 MOYEN — Cache MSI. Action : VÉRIFIER AVANT.
[60] C:\ProgramData\Autodesk\ADPSDK — 🔴 ÉLEVÉ — Autodesk Desktop Platform SDK. Action : NE PAS TOUCHER.
[61] C:\ProgramData\Package Cache\{F9C5C994-...}v1.0.0.0 — 🟠 MOYEN — Cache MSI. Action : VÉRIFIER AVANT.
[62] C:\ProgramData\Package Cache\{5ED5DA89-...}v8.0.21.25475 — 🟠 MOYEN — Cache MSI .NET 8 Runtime. Action : VÉRIFIER AVANT.
[63] C:\ProgramData\Package Cache\{D0772A8A-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[64] C:\ProgramData\Package Cache\{DB72537E-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[65] C:\ProgramData\Package Cache\{EB362CC9-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[66] C:\ProgramData\Package Cache\{215198BD-...}v6.0.36.24516 — 🟠 MOYEN — Cache MSI .NET 6 Runtime. Action : VÉRIFIER AVANT.
[67] C:\ProgramData\Package Cache\{7C1561F8-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[68] C:\ProgramData\USOPrivate — ⛔ CRITIQUE — État privé d'Update Session Orchestrator. Action : NE PAS TOUCHER.
[69] C:\ProgramData\USOPrivate\UpdateStore — ⛔ CRITIQUE — Store des updates Windows. Action : NE PAS TOUCHER.
[70] C:\ProgramData\Microsoft\Diagnosis — ⛔ CRITIQUE — Composant Windows DiagTrack/télémétrie. Action : NE PAS TOUCHER.
[71] C:\ProgramData\Caphyon — 🟡 FAIBLE — Données Advanced Installer (Caphyon). Action : VÉRIFIER AVANT.
[72] C:\ProgramData\Caphyon\Advanced Installer — 🟡 FAIBLE — Données Advanced Installer. Action : VÉRIFIER AVANT.
[73] C:\ProgramData\Apple\Installer Cache — 🟠 MOYEN — Cache des installeurs Apple. Action : VÉRIFIER AVANT.
[74] C:\ProgramData\Intel — 🔴 ÉLEVÉ — Données partagées Intel (drivers, XTU, GCC). Action : NE PAS TOUCHER.
[75] C:\ProgramData\Intel\Intel Extreme Tuning Utility — 🟠 MOYEN — Données Intel XTU. Action : VÉRIFIER AVANT.
[76] C:\ProgramData\Dynamo — 🔴 ÉLEVÉ — Dynamo (visual programming pour Revit/Civil 3D). Action : NE PAS TOUCHER.
[77] C:\ProgramData\Dynamo\Dynamo Core — 🔴 ÉLEVÉ — Cœur de Dynamo. Action : NE PAS TOUCHER.
[78] C:\ProgramData\Package Cache\{37B8F9C7-...}v11.0.61030 — 🟠 MOYEN — Cache MSI VC++ 2012 Redistributable. Action : VÉRIFIER AVANT.
[79] C:\ProgramData\Package Cache\{AECD4ED0-...}v14.50.35719 — 🟠 MOYEN — Cache MSI VC++ 2015-2022 Redistributable. Action : VÉRIFIER AVANT.
[80] C:\ProgramData\Package Cache\{010792BA-...}v12.0.40664 — 🟠 MOYEN — Cache MSI VC++ 2013 Redistributable. Action : VÉRIFIER AVANT.
[81] C:\ProgramData\Package Cache\{929FBD26-...}v12.0.21005 — 🟠 MOYEN — Cache MSI VC++ 2013 Redistributable. Action : VÉRIFIER AVANT.
[82] C:\ProgramData\Package Cache\{773AD50D-...}v14.50.35719 — 🟠 MOYEN — Cache MSI VC++ 2015-2022 Redistributable. Action : VÉRIFIER AVANT.
[83] C:\ProgramData\Package Cache\{B175520C-...}v11.0.61030 — 🟠 MOYEN — Cache MSI VC++ 2012 Redistributable. Action : VÉRIFIER AVANT.
[84] C:\ProgramData\Package Cache\{96B30005-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[85] C:\ProgramData\NVIDIA Corporation\Drs — 🔴 ÉLEVÉ — Driver Setting profiles NVIDIA. Action : NE PAS TOUCHER.
[86] C:\ProgramData\Package Cache\{F8CFEB22-...}v12.0.21005 — 🟠 MOYEN — Cache MSI VC++ 2013 Redistributable. Action : VÉRIFIER AVANT.
[87] C:\ProgramData\HP\StreamLog — 🟠 MOYEN — Logs HP. Action : VÉRIFIER AVANT.
[88] C:\ProgramData\printix.net — 🔴 ÉLEVÉ — Printix (gestion impression cloud d'entreprise). Action : NE PAS TOUCHER.
[89] C:\ProgramData\Package Cache\{F9DDDEF1-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[90] C:\ProgramData\Package Cache\{06671634-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[91] C:\ProgramData\Package Cache\{26BD6DC6-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[92] C:\ProgramData\bdkitinstaller — 🔴 ÉLEVÉ — Bitdefender Kit Installer corporate. Action : NE PAS TOUCHER.
[93] C:\ProgramData\Package Cache\{E8BE09DF-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[94] C:\ProgramData\Autodesk\CER — 🔴 ÉLEVÉ — Customer Error Reporting Autodesk. Action : NE PAS TOUCHER.
[95] C:\ProgramData\Package Cache\{7E52FE9F-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[96] C:\ProgramData\Package Cache\{20C01991-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[97] C:\ProgramData\Microsoft\DefaultPackMSI — ⛔ CRITIQUE — Pack MSI par défaut Microsoft. Action : NE PAS TOUCHER.
[98] C:\ProgramData\Package Cache\{F251EBD6-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[99] C:\ProgramData\Package Cache\{368E4D03-...}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[100] C:\ProgramData\HP\Downloaded Installations — 🟠 MOYEN — Cache d'installeurs HP téléchargés. Action : VÉRIFIER AVANT.

## Items 101-200

[101-103, 106, 114, 116, 119, 121-122, 126-127, 131, 138, 143, 158, 161, 163-165, 168, 180-181, 184, 188, 190-195] : tous Package Cache\{GUID}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[104] C:\ProgramData\dbg — 🟡 FAIBLE — Symboles de débogage. Action : CORBEILLE OK.
[105] C:\ProgramData\dbg\sym — 🟡 FAIBLE — Sous-dossier symboles. Action : CORBEILLE OK.
[107] C:\ProgramData\Unifi Export HRSINTRA_tanguy.delrieu — 🟠 MOYEN — Export Unifi à ton nom. Action : VÉRIFIER AVANT.
[108] C:\ProgramData\Microsoft\DiagnosticLogCSP — ⛔ CRITIQUE — Diagnostic Log CSP MDM. Action : NE PAS TOUCHER.
[109] C:\ProgramData\Claude — 🔴 ÉLEVÉ — Application Claude Desktop active. Action : NE PAS TOUCHER.
[110] C:\ProgramData\Claude\Logs — 🟡 FAIBLE — Logs de Claude Desktop. Action : VÉRIFIER AVANT.
[111] C:\ProgramData\DiRoots.One\Settings — 🔴 ÉLEVÉ — Préférences/licences DiRoots.One. Action : NE PAS TOUCHER.
[112] C:\ProgramData\NVIDIA Corporation\nvtopps — 🔴 ÉLEVÉ — NVIDIA Telemetry/Optimal Playable Settings. Action : NE PAS TOUCHER.
[113] C:\ProgramData\HP\HP_DockAccessory — 🟠 MOYEN — Données HP Dock. Action : VÉRIFIER AVANT.
[115] C:\ProgramData\NinjaRMMAgent\tzdata — 🔴 ÉLEVÉ — Données timezone NinjaOne. Action : NE PAS TOUCHER.
[117] C:\ProgramData\PlaceMaker Revit — 🔴 ÉLEVÉ — Plugin PlaceMaker pour Revit. Action : NE PAS TOUCHER.
[118] C:\ProgramData\PlaceMaker Revit\Images — 🔴 ÉLEVÉ — Cache images PlaceMaker. Action : NE PAS TOUCHER.
[120] C:\ProgramData\Windows App Certification Kit — 🟡 FAIBLE — WACK. Action : VÉRIFIER AVANT.
[123] C:\ProgramData\Microsoft\Windows Security Health — ⛔ CRITIQUE — État Windows Security/Defender. Action : NE PAS TOUCHER.
[124] C:\ProgramData\Package Cache\{c0b93b80-d0a4-47fc-a4d1-21a4008a63ca} — 🟠 MOYEN — Cache Burn VS Build Tools. Action : VÉRIFIER AVANT.
[125] C:\ProgramData\Autodesk\AcNGEN — 🔴 ÉLEVÉ — AutoCAD .NET pré-compilation cache. Action : NE PAS TOUCHER.
[128] C:\ProgramData\NinjaRMMAgent\jsonoutput — 🔴 ÉLEVÉ — Sortie JSON NinjaOne agent corporate. Action : NE PAS TOUCHER.
[129] C:\ProgramData\Package Cache\{b4f31c4b-637c-4eca-8a82-133fbb5b3eda} — 🟠 MOYEN — Cache Burn VS Build Tools. Action : VÉRIFIER AVANT.
[130] C:\ProgramData\Microsoft\Device Stage — ⛔ CRITIQUE — Composant Windows. Action : NE PAS TOUCHER.
[132] C:\ProgramData\Microsoft\User Account Pictures — ⛔ CRITIQUE — Photos de compte Windows. Action : NE PAS TOUCHER.
[133-136, 142, 145, 149-150, 152, 155-157, 159-160, 162] : Package Cache\{GUID}vXX.X (VC++ Redistributables divers) — 🟠 MOYEN — Action : VÉRIFIER AVANT.
[137] C:\ProgramData\DiRoots.One\LangResources — 🔴 ÉLEVÉ — Ressources langues DiRoots.One. Action : NE PAS TOUCHER.
[139] C:\ProgramData\Splashtop — 🔴 ÉLEVÉ — Splashtop Remote corporate. Action : NE PAS TOUCHER.
[140] C:\ProgramData\Package Cache\{F3849101-...}v80.8.45513 — 🟠 MOYEN — VC++ Redistributable. Action : VÉRIFIER AVANT.
[141] C:\ProgramData\DiRoots.ProSheets\Settings — 🔴 ÉLEVÉ — Préférences ProSheets. Action : NE PAS TOUCHER.
[144] C:\ProgramData\Splashtop\Splashtop Remote Client for STB — 🔴 ÉLEVÉ — Splashtop corporate. Action : NE PAS TOUCHER.
[146-148, 151, 169, 171-179, 183, 189, 196-200] : Package Cache\{GUID} ou Cache Burn — 🟠 MOYEN — Action : VÉRIFIER AVANT.
[154] C:\ProgramData\Autodesk\AdSSO — ⛔ CRITIQUE — Autodesk Single Sign-On (tokens auth). Action : NE PAS TOUCHER.
[166] C:\ProgramData\Microsoft\SmsRouter — ⛔ CRITIQUE — Composant Windows téléphonie SMS. Action : NE PAS TOUCHER.
[167] C:\ProgramData\Microsoft\Windows NT — ⛔ CRITIQUE — Configuration Windows NT. Action : NE PAS TOUCHER.
[170] C:\ProgramData\Microsoft\Crypto — ⛔ CRITIQUE — Clés et certificats cryptographiques machine. Action : NE PAS TOUCHER.
[182] C:\ProgramData\Microsoft\Storage Health — ⛔ CRITIQUE — Service Windows Storage Health. Action : NE PAS TOUCHER.
[185] C:\ProgramData\bdkitinstaller\{7c464d4f-...} — 🔴 ÉLEVÉ — Cache installeur Bitdefender. Action : NE PAS TOUCHER.
[186] C:\ProgramData\NinjaRMMAgent\storage — 🔴 ÉLEVÉ — Storage NinjaOne agent corporate. Action : NE PAS TOUCHER.
[187] C:\ProgramData\HP\logs — 🟡 FAIBLE — Logs HP. Action : VÉRIFIER AVANT.

## Items 201-300

[201-214] : Package Cache\{GUID}v10.1.26100.7705 — ⛔ CRITIQUE — Windows SDK. Action : NE PAS TOUCHER.
[215] C:\ProgramData\Autodesk\Adlm — ⛔ CRITIQUE — Autodesk License Manager. Action : NE PAS TOUCHER.
[216] C:\ProgramData\Autodesk\IDSDK — 🔴 ÉLEVÉ — Autodesk Identity SDK SSO. Action : NE PAS TOUCHER.
[217] C:\ProgramData\Packages — ⛔ CRITIQUE — Stockage packages AppX/MSIX Windows. Action : NE PAS TOUCHER.
[218] C:\ProgramData\_Empirum\Adobe Systems — 🔴 ÉLEVÉ — Déploiement Adobe via Empirum corporate. Action : NE PAS TOUCHER.
[219] C:\ProgramData\Package Cache\{D71432C2-...}v6.17.21301 — 🟠 MOYEN — Cache MSI. Action : VÉRIFIER AVANT.
[220] C:\ProgramData\Microsoft\MapData — ⛔ CRITIQUE — Données cartes hors-ligne Windows. Action : NE PAS TOUCHER.
[221] C:\ProgramData\Package Cache\{13D2A7A2-...}v10.1.0.0 — 🟠 MOYEN — Cache MSI. Action : VÉRIFIER AVANT.
[222] C:\ProgramData\Microsoft\GroupPolicy — ⛔ CRITIQUE — Politiques de groupe corporate AD. Action : NE PAS TOUCHER.
[223] C:\ProgramData\Intel\GCC — 🔴 ÉLEVÉ — Intel Graphics Command Center. Action : NE PAS TOUCHER.
[224] C:\ProgramData\NinjaRMMAgent\policy — 🔴 ÉLEVÉ — Politiques NinjaOne corporate. Action : NE PAS TOUCHER.
[225] C:\ProgramData\Microsoft\WPD — ⛔ CRITIQUE — Windows Portable Devices. Action : NE PAS TOUCHER.
[226] C:\ProgramData\UPGREAT — 🔴 ÉLEVÉ — UPGREAT (intégrateur IT corporate). Action : NE PAS TOUCHER.
[227] C:\ProgramData\UPGREAT\Scripts — 🔴 ÉLEVÉ — Scripts corporate UPGREAT. Action : NE PAS TOUCHER.
[228] C:\ProgramData\Microsoft\Group Policy — ⛔ CRITIQUE — Stratégies de groupe corporate. Action : NE PAS TOUCHER.
[229-231, 233-238, 242, 244, 248-249] : C:\ProgramData\Windows App Certification Kit\<locale> — 🟡 FAIBLE — Localisations WACK. Action : VÉRIFIER AVANT.
[232] C:\ProgramData\Autodesk\RVT 2022 — 🟠 MOYEN — Données partagées Revit 2022. Action : VÉRIFIER AVANT.
[239] C:\ProgramData\NVIDIA Corporation\DisplayDriverRAS — 🔴 ÉLEVÉ — NVIDIA Display Driver Reliability Analysis. Action : NE PAS TOUCHER.
[240] C:\ProgramData\_Empirum\RuckZuck — 🔴 ÉLEVÉ — RuckZuck déploiement logiciel corporate. Action : NE PAS TOUCHER.
[241] C:\ProgramData\HP\bridge — 🟠 MOYEN — HP Bridge. Action : VÉRIFIER AVANT.
[243] C:\ProgramData\HP\HP TechPulse SmartHealth — 🔴 ÉLEVÉ — HP TechPulse monitoring corporate. Action : NE PAS TOUCHER.
[245] C:\ProgramData\Splashtop\Common — 🔴 ÉLEVÉ — Composants Splashtop corporate. Action : NE PAS TOUCHER.
[246] C:\ProgramData\FileOpen — 🟠 MOYEN — FileOpen plug-in DRM PDF. Action : VÉRIFIER AVANT.
[247] C:\ProgramData\FileOpen\Updates — 🟠 MOYEN — Updates FileOpen DRM. Action : VÉRIFIER AVANT.
[250] C:\ProgramData\Packages\AD2F1837.myHP_v10z8vjag6ke6 — 🔴 ÉLEVÉ — UWP myHP. Action : NE PAS TOUCHER.
[251] C:\ProgramData\DiRoots.ProSheets\LangResources — 🔴 ÉLEVÉ — Ressources langues ProSheets. Action : NE PAS TOUCHER.
[252] C:\ProgramData\_Empirum\Printix.net — 🔴 ÉLEVÉ — Déploiement Printix Empirum corporate. Action : NE PAS TOUCHER.
[253] C:\ProgramData\ABBYY — 🟠 MOYEN — ABBYY OCR/FineReader. Action : VÉRIFIER AVANT.
[254] C:\ProgramData\Packages\MSTeams_8wekyb3d8bbwe — 🔴 ÉLEVÉ — Microsoft Teams UWP active. Action : NE PAS TOUCHER.
[255] C:\ProgramData\Ideate — 🔴 ÉLEVÉ — Ideate Software (plugins Revit). Action : NE PAS TOUCHER.
[256] C:\ProgramData\Ideate\Ideate Automation — 🔴 ÉLEVÉ — Ideate Automation. Action : NE PAS TOUCHER.
[257] C:\ProgramData\Intel_XMM7560 Driver Package — 🔴 ÉLEVÉ — Driver Intel XMM7560 modem WWAN. Action : NE PAS TOUCHER.
[258] C:\ProgramData\Intel_XMM7560 Driver Package\logs — 🟠 MOYEN — Logs driver WWAN. Action : VÉRIFIER AVANT.
[259] C:\ProgramData\Microsoft\MF — ⛔ CRITIQUE — Media Foundation Windows. Action : NE PAS TOUCHER.
[260] C:\ProgramData\Microsoft\IdentityCRL — ⛔ CRITIQUE — Identity Certificate Revocation List. Action : NE PAS TOUCHER.
[261] C:\ProgramData\NinjaRMMAgent\uscripting — 🔴 ÉLEVÉ — Scripts utilisateur NinjaOne corporate. Action : NE PAS TOUCHER.
[262] C:\ProgramData\Packages\5319275A.WhatsAppDesktop_cv1g1gvanyjgm — 🔴 ÉLEVÉ — WhatsApp Desktop UWP. Action : NE PAS TOUCHER.
[263] C:\ProgramData\Packages\Claude_pzs8sxrjxfjjc — 🔴 ÉLEVÉ — Claude Desktop UWP. Action : NE PAS TOUCHER.
[264] C:\ProgramData\Packages\PythonSoftwareFoundation.PythonManager_qbz5n2kfra8p0 — 🔴 ÉLEVÉ — Python Manager Windows Store. Action : NE PAS TOUCHER.
[265] C:\ProgramData\NinjaRMMAgent\scripting — 🔴 ÉLEVÉ — Scripts NinjaOne corporate. Action : NE PAS TOUCHER.
[266] C:\ProgramData\Microsoft\SmartCard — ⛔ CRITIQUE — Données SmartCard authentification PKI. Action : NE PAS TOUCHER.
[267] C:\ProgramData\Autodesk\ContentCatalog — 🔴 ÉLEVÉ — Catalogue de contenu Autodesk. Action : NE PAS TOUCHER.
[268] C:\ProgramData\Autodesk\DWG TrueView 2022 - English — 🟠 MOYEN — DWG TrueView 2022. Action : VÉRIFIER AVANT.
[269] C:\ProgramData\_Empirum\Malwarebytes — 🔴 ÉLEVÉ — Déploiement Malwarebytes Empirum corporate. Action : NE PAS TOUCHER.
[270] C:\ProgramData\Autodesk\Revit Steel Connections 2023 — 🟠 MOYEN — Composant Revit 2023. Action : VÉRIFIER AVANT.
[271] C:\ProgramData\Microsoft\Wlansvc — ⛔ CRITIQUE — Profils Wi-Fi Windows (SSID + clés WPA). Action : NE PAS TOUCHER.
[272] C:\ProgramData\Microsoft\Phone Tools — ⛔ CRITIQUE — Outils téléphonie Windows. Action : NE PAS TOUCHER.
[273] C:\ProgramData\_Empirum\NinjaOne — 🔴 ÉLEVÉ — Déploiement NinjaOne Empirum. Action : NE PAS TOUCHER.
[274] C:\ProgramData\HP\registration — 🟠 MOYEN — Données enregistrement HP. Action : VÉRIFIER AVANT.
[275] C:\ProgramData\Cisco — 🔴 ÉLEVÉ — Cisco AnyConnect VPN corporate. Action : NE PAS TOUCHER.
[276] C:\ProgramData\Cisco\Cisco AnyConnect Secure Mobility Client — 🔴 ÉLEVÉ — VPN corporate. Action : NE PAS TOUCHER.
[277] C:\ProgramData\Microsoft\AppV — ⛔ CRITIQUE — Microsoft Application Virtualization. Action : NE PAS TOUCHER.
[278] C:\ProgramData\Autodesk\Revit Interoperability 2023 — 🟡 FAIBLE — Interop Revit 2023 vide. Action : VÉRIFIER AVANT.
[279] C:\ProgramData\Autodesk\Revit Interoperability 2024 — 🟡 FAIBLE — Interop Revit 2024 vide. Action : VÉRIFIER AVANT.
[280] C:\ProgramData\HP\SharedServices — 🟠 MOYEN — Services partagés HP. Action : VÉRIFIER AVANT.
[281] C:\ProgramData\Microsoft\Event Viewer — ⛔ CRITIQUE — Configuration Observateur d'événements Windows. Action : NE PAS TOUCHER.
[282] C:\ProgramData\hpqlog — 🟡 FAIBLE — Logs HP héritage. Action : CORBEILLE OK.
[283] C:\ProgramData\BEXEL\Building Explorer 5 — 🟠 MOYEN — BEXEL Manager BIM. Action : VÉRIFIER AVANT.
[284] C:\ProgramData\NVIDIA Corporation\NvProfileUpdaterPlugin — 🔴 ÉLEVÉ — Composant driver NVIDIA. Action : NE PAS TOUCHER.
[285] C:\ProgramData\KONICA MINOLTA — 🟠 MOYEN — Données driver imprimante Konica Minolta. Action : VÉRIFIER AVANT.
[286] C:\ProgramData\Epic — 🟡 FAIBLE — Epic Online Services. Action : VÉRIFIER AVANT.
[287] C:\ProgramData\Epic\EpicOnlineServices — 🟡 FAIBLE — Idem [286]. Action : VÉRIFIER AVANT.
[288] C:\ProgramData\Autodesk\RevitLT — 🟡 FAIBLE — Données Revit LT vide. Action : VÉRIFIER AVANT.
[289] C:\ProgramData\Microsoft\Vault — ⛔ CRITIQUE — Windows Credential Vault. Action : NE PAS TOUCHER.
[290] C:\ProgramData\.keentools — 🟡 FAIBLE — KeenTools plugins 3D. Action : VÉRIFIER AVANT.
[291] C:\ProgramData\PlaceMaker Revit\Logs — 🟡 FAIBLE — Logs PlaceMaker vide. Action : CORBEILLE OK.
[292] C:\ProgramData\Ideate\License — ⛔ CRITIQUE — Licences Ideate Software. Action : NE PAS TOUCHER.
[293] C:\ProgramData\Apple Computer — 🟠 MOYEN — Données partagées iTunes/iCloud. Action : VÉRIFIER AVANT.
[294] C:\ProgramData\Apple Computer\iTunes — 🟠 MOYEN — Données iTunes partagées. Action : VÉRIFIER AVANT.
[295] C:\ProgramData\IsolatedStorage — 🟠 MOYEN — .NET Isolated Storage. Action : VÉRIFIER AVANT.
[296] C:\ProgramData\IsolatedStorage\vj3lbu5a.aaz — 🟠 MOYEN — Sous-dossier IsolatedStorage. Action : VÉRIFIER AVANT.
[297] C:\ProgramData\Autodesk\CADManagerCtrlUtility — 🔴 ÉLEVÉ — Autodesk CAD Manager Control Utility. Action : NE PAS TOUCHER.
[298] C:\ProgramData\Autodesk\AddinProcessIds — 🔴 ÉLEVÉ — État process add-ins Autodesk. Action : NE PAS TOUCHER.
[299] C:\ProgramData\USOPrivate\ExpeditedAppRegistrations — ⛔ CRITIQUE — Composant Windows Update. Action : NE PAS TOUCHER.
[300] C:\ProgramData\Autodesk\Revit Steel Connections 2024 — 🟡 FAIBLE — Steel Connections 2024 vide. Action : VÉRIFIER AVANT.

## Items 301-365

[301] C:\ProgramData\Microsoft\WDF — ⛔ CRITIQUE — Windows Driver Framework. Action : NE PAS TOUCHER.
[302] C:\ProgramData\Microsoft\WinGet — ⛔ CRITIQUE — Windows Package Manager. Action : NE PAS TOUCHER.
[303] C:\ProgramData\FileMaker\FileMaker Pro Advanced — 🟡 FAIBLE — FileMaker vide. Action : VÉRIFIER AVANT.
[304] C:\ProgramData\MSS — 🟡 FAIBLE — Microsoft Security Scanner ou vestige. Action : VÉRIFIER AVANT.
[305] C:\ProgramData\TechSmith\Updater — 🟡 FAIBLE — Updater TechSmith Snagit/Camtasia. Action : VÉRIFIER AVANT.
[306] C:\ProgramData\Microsoft DevDiv\Installation — ⛔ CRITIQUE — Composant d'installation Visual Studio. Action : NE PAS TOUCHER.
[307] C:\ProgramData\BIM Vision — 🟡 FAIBLE — BIM Vision visualiseur IFC vide. Action : VÉRIFIER AVANT.
[308] C:\ProgramData\File System Monitor — 🟠 MOYEN — Marker monitor fichiers. Action : VÉRIFIER AVANT.
[309] C:\ProgramData\48C4687D-9760-4F5B-BAB3-60351B0841E4 — 🟢 AUCUN — Dossier vide nom GUID vestige. Action : SUPPRIMER.
[310] C:\ProgramData\Adobe — 🟢 AUCUN — Dossier vide. Action : SUPPRIMER.
[311] C:\ProgramData\Autodesk\Downloads — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[312] C:\ProgramData\Autodesk\Inventor Interoperability 2024 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[313] C:\ProgramData\Autodesk\LicensingAnalyticsClient — 🟡 FAIBLE — Vide lié au système de licence Autodesk. Action : CORBEILLE OK.
[314] C:\ProgramData\Autodesk\Navisworks Manage 2023 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[315] C:\ProgramData\Autodesk\Navisworks Manage 2024 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[316] C:\ProgramData\Autodesk\Revit Server 2021 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[317] C:\ProgramData\Autodesk\Revit Server 2022 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[318] C:\ProgramData\Autodesk\Revit Server 2023 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[319] C:\ProgramData\Autodesk\Revit Server 2024 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[320] C:\ProgramData\Autodesk\Revit Server 2025 — 🟠 MOYEN — Vide MAIS Revit 2025 actif. Action : GARDER.
[321] C:\ProgramData\Autodesk\Revit Steel Connections 2021 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[322] C:\ProgramData\Autodesk\Revit Steel Connections 2022 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[323] C:\ProgramData\Autodesk\RevitServerTool — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[324] C:\ProgramData\Autodesk\Rx_Navisworks Manage — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[325] C:\ProgramData\Autodesk\Rx_RevitExtractor — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[326] C:\ProgramData\bdlogging — 🔴 ÉLEVÉ — Vide MAIS lié à Bitdefender corporate. Action : NE PAS TOUCHER.
[327] C:\ProgramData\boost_interprocess — 🟡 FAIBLE — Marker IPC Boost. Action : CORBEILLE OK.
[328] C:\ProgramData\boost_interprocess\A8010000 — 🟡 FAIBLE — Idem [327]. Action : CORBEILLE OK.
[329] C:\ProgramData\Citrix\Receiver — 🟢 AUCUN — Vide Citrix désinstallé. Action : SUPPRIMER.
[330] C:\ProgramData\Datacubist — 🟢 AUCUN — Vide Simplebim désinstallé. Action : SUPPRIMER.
[331] C:\ProgramData\Datacubist\Simplebim 10 — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[332] C:\ProgramData\FLEXnet — ⛔ CRITIQUE — FlexLM/FlexNet manager licences flottantes. Action : NE PAS TOUCHER.
[333] C:\ProgramData\HP\ExternalApps — 🟢 AUCUN — Vide. Action : SUPPRIMER.
[334] C:\ProgramData\HP\HP Image Assistant — 🟢 AUCUN — Vide HP Image Assistant désinstallé. Action : SUPPRIMER.
[335] C:\ProgramData\KUBUS\BCF Managers 6.5 - Revit 2021 - 2024 6.5.5 — 🟠 MOYEN — Vide MAIS install actuel KUBUS lié. Action : GARDER.
[336] C:\ProgramData\Microsoft\DeviceSync — ⛔ CRITIQUE — Composant Windows. Action : NE PAS TOUCHER.
[337] C:\ProgramData\Microsoft\DRM — ⛔ CRITIQUE — Digital Rights Management Windows. Action : NE PAS TOUCHER.
[338] C:\ProgramData\Microsoft\NetFramework — ⛔ CRITIQUE — .NET Framework composant Windows. Action : NE PAS TOUCHER.
[339] C:\ProgramData\Microsoft\Provisioning — ⛔ CRITIQUE — Provisioning Windows déploiement OS. Action : NE PAS TOUCHER.
[340] C:\ProgramData\Microsoft\Settings — ⛔ CRITIQUE — Settings Windows. Action : NE PAS TOUCHER.
[341] C:\ProgramData\Microsoft\Spectrum — ⛔ CRITIQUE — Composant Windows subsystem. Action : NE PAS TOUCHER.
[342] C:\ProgramData\Microsoft\Speech_OneCore — ⛔ CRITIQUE — Reconnaissance vocale Windows. Action : NE PAS TOUCHER.
[343] C:\ProgramData\Microsoft\Windows Defender Advanced Threat Protection — ⛔ CRITIQUE — Defender ATP / MDE sécurité endpoint. Action : NE PAS TOUCHER.
[344] C:\ProgramData\Microsoft\WinMSIPC — ⛔ CRITIQUE — Microsoft Information Protection Client RMS. Action : NE PAS TOUCHER.
[345] C:\ProgramData\Microsoft\WwanSvc — ⛔ CRITIQUE — Service WWAN 4G/5G modem. Action : NE PAS TOUCHER.
[346] C:\ProgramData\Microsoft OneDrive\setup — ⛔ CRITIQUE — Composant OneDrive sync entreprise. Action : NE PAS TOUCHER.
[347] C:\ProgramData\NinjaRMMAgent\pre-download_cache — 🔴 ÉLEVÉ — Cache pré-download NinjaOne corporate. Action : NE PAS TOUCHER.
[348] C:\ProgramData\NinjaRMMAgent\udownload — 🔴 ÉLEVÉ — Composant download NinjaOne. Action : NE PAS TOUCHER.
[349] C:\ProgramData\NVIDIA Corporation\GameSessionTelemetry — 🟡 FAIBLE — Vide télémétrie GeForce gaming. Action : CORBEILLE OK.
[350] C:\ProgramData\NVIDIA Corporation\umdlogs — 🟡 FAIBLE — User Mode Driver logs NVIDIA vide. Action : CORBEILLE OK.
[351] C:\ProgramData\Packages\AD2F1837.HPDisplayCenter_v10z8vjag6ke6 — 🟠 MOYEN — Package HP Display Center vide. Action : VÉRIFIER AVANT.
[352] C:\ProgramData\Packages\AppleInc.iCloud_nzyj5cx40ttqa — 🟠 MOYEN — Package iCloud UWP vide. Action : VÉRIFIER AVANT.
[353] C:\ProgramData\Packages\AppUp.IntelGraphicsExperience_8j3eq9eme6ctt — 🟠 MOYEN — Intel Graphics Experience UWP. Action : VÉRIFIER AVANT.
[354] C:\ProgramData\Packages\AppUp.IntelManagementandSecurityStatus_8j3eq9eme6ctt — 🔴 ÉLEVÉ — Intel Management Engine status. Action : NE PAS TOUCHER.
[355] C:\ProgramData\Packages\com.owllabs.meetingowl.windows_cb17j8khw3v66 — 🟡 FAIBLE — Owl Labs Meeting Owl. Action : VÉRIFIER AVANT.
[356] C:\ProgramData\Packages\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj — 🔴 ÉLEVÉ — NVIDIA Control Panel UWP. Action : NE PAS TOUCHER.
[357] C:\ProgramData\Packages\OwlLabs.MeetingOwl_pkza4zvy68k6r — 🟡 FAIBLE — Idem [355]. Action : VÉRIFIER AVANT.
[358] C:\ProgramData\restored_quar — 🔴 ÉLEVÉ — Quarantaine restaurée d'antivirus Bitdefender. Action : NE PAS TOUCHER.
[359] C:\ProgramData\RevitInterProcess — 🔴 ÉLEVÉ — Communication inter-process Revit. Action : NE PAS TOUCHER.
[360] C:\ProgramData\RevitInterProcess\20250522080631.500000 — 🟡 FAIBLE — Snapshot IPC Revit. Action : VÉRIFIER AVANT.
[361] C:\ProgramData\SoftwareDistribution — ⛔ CRITIQUE — Windows Update Software Distribution. Action : NE PAS TOUCHER.
[362] C:\ProgramData\ssh — ⛔ CRITIQUE — Configuration SSH système (host keys). Action : NE PAS TOUCHER.
[363] C:\ProgramData\Tracker Software\PDFXShellExt — 🟡 FAIBLE — Tracker Software PDF-XChange shell. Action : VÉRIFIER AVANT.
[364] C:\ProgramData\WindowsHolographicDevices — ⛔ CRITIQUE — Composant Windows Mixed Reality. Action : NE PAS TOUCHER.
[365] C:\ProgramData\WindowsHolographicDevices\SpatialStore — ⛔ CRITIQUE — Composant Windows Mixed Reality. Action : NE PAS TOUCHER.

---

## Distribution mesurée (verdict humain sur 365 items)

| RiskLevel | Count | % | RecommendedAction dominante |
|---|---|---|---|
| Critique | ~165 | ~45 % | NePasToucher |
| Eleve | ~95 | ~26 % | NePasToucher |
| Moyen | ~80 | ~22 % | VerifierAvant |
| Faible | ~17 | ~5 % | CorbeilleOk / VerifierAvant |
| Aucun | ~17 | ~5 % | Supprimer |
| Garder (explicite) | 2 | <1 % | Garder |

**Note** : les items dans la plage [101-200] et [201-300] avec annotations groupées (`[101-103, 106, 114, ...]`) sont à expanser un par un dans le JSON fixture. L'exécuteur du plan 10-05 doit lire ce fichier et générer un objet JSON par numéro [N] de l'audit.

---

*Source brute conservée pour audit reproductible. Tout enrichissement du moteur de scoring doit être validé contre cette baseline humaine.*
