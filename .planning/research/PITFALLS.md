# Pitfalls Research — DiskScout

**Domain:** Windows WPF desktop disk analyzer (P/Invoke filesystem scan + registry reading + long-path handling + admin elevation + single-file publish)
**Researched:** 2026-04-24
**Confidence:** HIGH (Win32 filesystem, registry, WPF virtualization are mature well-documented domains; single-file + trimming interactions with P/Invoke are MEDIUM confidence because they still shift across .NET versions)

This document enumerates failure modes that historically sink disk-scanner projects (WinDirStat / TreeSize / WizTree lineage) and the specific gotchas of combining WPF + P/Invoke + registry + single-file publish on Windows 10/11 in 2026. Every pitfall here has bitten real code; none are hypothetical.

---

## Critical Pitfalls

### Pitfall 1: Infinite loop / double-count via reparse points, junctions, symlinks

**What goes wrong:**
The scanner walks into a junction or symbolic link whose target is an ancestor directory (or another directory on the same volume), producing either an infinite descent that never terminates, a stack overflow, or a total size that is 2x–10x the real disk usage. Classic offender on Windows: `C:\Users\All Users` (junction to `C:\ProgramData`), `C:\Documents and Settings` (junction to `C:\Users` — access denied but still a junction), `C:\Users\<user>\Application Data` (junction to `AppData\Roaming`), `C:\Users\<user>\My Documents` (junction to `Documents`), plus user-created OneDrive/Dropbox/Steam library junctions.

**Why it happens:**
`WIN32_FIND_DATA.dwFileAttributes` contains `FILE_ATTRIBUTE_REPARSE_POINT` and developers either (a) don't check it, (b) check it but follow anyway, or (c) check it but don't distinguish mount points / symlinks / junctions / OneDrive placeholders. `FindFirstFileEx` returns the reparse point metadata (not the target), so if you recurse into the name you end up following the reparse at the OS level.

**How to avoid:**
- On every entry returned by `FindNextFile`, test `(dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0`. If set, **do not recurse** by default.
- Inspect `dwReserved0` on reparse entries to get the reparse tag and differentiate: `IO_REPARSE_TAG_SYMLINK` (0xA000000C), `IO_REPARSE_TAG_MOUNT_POINT` (0xA0000003, junctions + volume mount points), `IO_REPARSE_TAG_CLOUD` family (0x9000001A etc., OneDrive/Dropbox placeholders), `IO_REPARSE_TAG_APPEXECLINK` (0x8000001B, MS Store app aliases in `%LocalAppData%\Microsoft\WindowsApps`).
- For DiskScout: count the reparse point's own on-disk size (usually 0) and stop. Tag the node as `IsReparsePoint = true` so the UI can display it with a chain icon and a note "not traversed".
- Optional: maintain a `HashSet<(VolumeSerial, FileIndex)>` (built from `BY_HANDLE_FILE_INFORMATION` or `FILE_ID_INFO`) to detect re-entry even without the reparse flag.

**Warning signs:**
Scan never finishes on `C:`. Total size > physical disk capacity. Depth counter > 40. Same path appears twice in the tree.

**Phase to address:**
Phase 2 (P/Invoke scanner core). Before any parallelization.

**Severity:** Critical

---

### Pitfall 2: MAX_PATH (260) silent truncation / `PathTooLongException`

**What goes wrong:**
The scanner fails on paths > 260 chars with `PathTooLongException` (managed) or `ERROR_FILENAME_EXCED_RANGE` / `ERROR_PATH_NOT_FOUND` (native). Nodes in deep directory trees (npm `node_modules`, nested Git repos, `WinSxS`, backed-up user folders) silently disappear from the tree, and their sizes vanish from the total. Users blame the tool for "missing 40 GB".

**Why it happens:**
The ANSI `FindFirstFile` caps at 260. Even `FindFirstFileW` caps at 260 unless you pass the `\\?\` prefix. Path concatenation via `Path.Combine` does not add the prefix. On Windows 10 1607+, `LongPathsEnabled` opt-in exists in the registry but is **not on by default** on most machines, and it only affects the managed BCL, not your raw P/Invoke calls.

**How to avoid:**
- Always build search paths with the `\\?\` (for local) and `\\?\UNC\` (for network) prefix when calling `FindFirstFileEx` / `FindFirstFileExW`. Applies to the **search pattern** too — not just results.
- Keep the prefix internally; strip it only for UI display.
- Cache the prefix on the root handle so you don't re-prepend incorrectly (`\\?\\\?\C:\...` is a common bug).
- Add `<longPathAware>true</longPathAware>` to `app.manifest` and target `net8.0-windows` — does not remove the need for `\\?\` in raw P/Invoke but improves managed fallbacks.
- Never use `Path.GetFullPath` on a `\\?\` path without guarding — it can strip the prefix.

**Warning signs:**
Total size < "Properties" dialog in Explorer. Log shows `PathTooLongException` or errno 206 / 3. Scan visibly skips `node_modules` or `WinSxS`.

**Phase to address:**
Phase 2 (P/Invoke scanner core) — must be baked in from the first FindFirstFile call. Retrofitting is painful.

**Severity:** Critical

---

### Pitfall 3: `FindClose` handle leak on exception / cancellation

**What goes wrong:**
A `FindFirstFileEx` handle is leaked because the code does `while (FindNextFile(...))` and an exception in the inner loop (or cancellation) escapes without calling `FindClose`. Over a full scan of a disk with millions of files this leaks thousands of kernel handles, eventually hitting the per-process handle limit (~16k) and producing `ERROR_TOO_MANY_OPEN_FILES` or outright process instability. On cancellation, leaked handles can also hold directory locks and prevent the user from deleting/moving scanned folders until DiskScout exits.

**Why it happens:**
`FindFirstFileEx` returns `INVALID_HANDLE_VALUE` (which is `-1`, **not** `IntPtr.Zero`) on failure. Developers often check `== IntPtr.Zero` and think they got a valid handle when they didn't, then try to `FindClose` an invalid handle. Or they use `using` on a raw `IntPtr` (which doesn't work), or they put the `FindClose` after the loop without `try/finally`.

**How to avoid:**
- Wrap the handle in a `SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid` with `ReleaseHandle()` calling `FindClose`. This is the canonical pattern and handles both invalid-handle sentinels plus finalizer cleanup.
- Always `try { ... } finally { handle.Dispose(); }` — never bare `while` loop without finally.
- Check `handle.IsInvalid` (not `== IntPtr.Zero`).
- Under cancellation, dispose the handle **before** propagating the `OperationCanceledException`.

**Warning signs:**
Handle count in Task Manager grows unboundedly during scan. `ERROR_NOT_ENOUGH_MEMORY` (8) or `ERROR_TOO_MANY_OPEN_FILES` (4). After scan cancellation, cannot delete/rename recently-scanned directories.

**Phase to address:**
Phase 2 (P/Invoke scanner core). Use `SafeFindHandle` from day one.

**Severity:** Critical

---

### Pitfall 4: WPF virtualization silently disabled → freeze on 100k nodes

**What goes wrong:**
User expands a large folder in the TreeView and the UI freezes for 30+ seconds, consumes 2+ GB of RAM, and never recovers on a 500 GB scan. The DataGrid listing files sorts column-by-click and freezes similarly because sorting forces realization of all rows.

**Why it happens (multiple silent killers):**
1. **Wrapping the TreeView in a `ScrollViewer`** — gives infinite height, defeats `VirtualizingStackPanel` (`codemag.com` anti-patterns article). Same for putting it in a `StackPanel`, a `Grid` row with `Height="Auto"`, or a `Grid.IsSharedSizeScope`.
2. **`VirtualizingStackPanel.IsVirtualizing="False"`** left from prototype code.
3. **`TreeViewItem` without `VirtualizingStackPanel.VirtualizationMode="Recycling"`** — default is `Standard`, which allocates new containers per scroll.
4. **Grouping enabled** in DataGrid (`CollectionView.GroupDescriptions`) silently disables virtualization unless `VirtualizingStackPanel.IsVirtualizingWhenGrouping="True"` is set.
5. **`ItemsSource` replaced wholesale** instead of incrementally updated — triggers full re-template.
6. **Sorting a `DataGrid` column** realizes every row to compute sort keys unless you sort on the underlying collection.
7. **`SetCurrentValue` / bindings on `Width="Auto"` columns** — `Auto` measures every realized item; use `*` or fixed widths for large grids.

**How to avoid:**
- `TreeView`: set `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"` explicitly. Verify no ancestor is a `ScrollViewer` / `StackPanel` / `Auto`-sized container. Use the TreeView's own `ScrollViewer` (it has one built in).
- `HierarchicalDataTemplate` with **lazy-loading children**: the `FolderNode.Children` ObservableCollection is populated on `TreeViewItem.Expanded`, not eagerly.
- `DataGrid`: `EnableRowVirtualization="True"`, `EnableColumnVirtualization="True"`, **fixed or `*` column widths only** (no `Auto`), sort via `ICollectionView.SortDescriptions` on the source (not built-in column sort that enumerates the visible grid).
- For flat file lists > 10k rows, consider a single pre-sorted `List<FileEntry>` + a `CollectionViewSource` and sort in a background task, then swap atomically.
- Benchmark explicitly: open a folder with 100k files and verify the TreeView expand < 500 ms.

**Warning signs:**
RAM > 1 GB with only a few nodes expanded. First scroll stutters for seconds. Column-click sort freezes UI. `VisualTreeHelper.GetChildrenCount` on the ItemsPresenter > visible items count.

**Phase to address:**
Phase 4 (UI / tree + grid). Validate with a synthetic 100k-entry dataset, not just real scans.

**Severity:** Critical

Sources: [Microsoft — Improve the Performance of a TreeView](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview), [Microsoft — Optimize control performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls), [CodeMag — XAML Anti-Patterns: Virtualization](https://codemag.com/Article/1407081/XAML-Anti-Patterns-Virtualization).

---

### Pitfall 5: `ObservableCollection` mutated from background scan thread → `InvalidOperationException`

**What goes wrong:**
Scan progress pushes new `FolderNode`s into an `ObservableCollection<FolderNode>` from the worker thread. WPF throws `NotSupportedException: "This type of CollectionView does not support changes to its SourceCollection from a thread different from the Dispatcher thread"` or `InvalidOperationException` mid-scan, killing the scan and leaving the UI in an inconsistent state. Alternatively (worse): no exception but random items missing, corrupted bindings, or visual glitches because `CollectionChanged` fires on the wrong thread.

**Why it happens:**
`ObservableCollection<T>` raises `CollectionChanged` on the thread that did the mutation. WPF bindings listen for it on the dispatcher thread. Developers try `BindingOperations.EnableCollectionSynchronization` but (a) forget to also gate the write side, (b) lock on the wrong object, or (c) use it on an `ICollectionView` wrapper that doesn't propagate.

**How to avoid:**
- **Pattern A (fastest, idiomatic)**: `BindingOperations.EnableCollectionSynchronization(collection, lockObject)` **on the UI thread at construction**, then every mutation from any thread is wrapped in `lock (lockObject) { collection.Add(...); }`. The collection itself is still populated off-thread — WPF marshals reads.
- **Pattern B (safest for tree)**: scan builds plain `List<FolderNode>` off-thread; once a subtree is complete, `Dispatcher.InvokeAsync(() => parent.Children = new ObservableCollection<FolderNode>(builtList))` — atomic swap, no cross-thread mutation.
- **Pattern C (throttled)**: scan pushes deltas into a `Channel<ScanUpdate>`; a UI-thread loop reads and applies in batches every 100 ms (fixes UI flood at 500k events/sec).
- **Never** use `Dispatcher.Invoke(...)` per file added — serializes the whole scan on the UI thread and wastes the P/Invoke speedup. Batch or swap.

**Warning signs:**
`NotSupportedException` in logs during scan. Progress counter jumps backwards. UI occasionally shows zero items then suddenly populates. Scan is only 10% faster than `Directory.EnumerateFiles` (UI marshaling dominates).

**Phase to address:**
Phase 3 (ViewModel + progress). Decide collection strategy before wiring scan → UI.

**Severity:** Critical

---

### Pitfall 6: Cancellation does not stop the native `FindNextFile` loop

**What goes wrong:**
User clicks "Cancel". The `CancellationToken` is triggered. The C# code checks `token.IsCancellationRequested` between directory enumerations — but the current `FindFirstFile` / `FindNextFile` loop is deep inside a 500k-entry directory (`WinSxS`, `node_modules`) and continues for another 30 seconds. Or worse: `Parallel.ForEach` at the top level already queued 8 root directories, and cancelling the token doesn't interrupt the workers already running; the user waits minutes for cancel to complete.

**Why it happens:**
P/Invoke calls are blocking and not cancellable. `CancellationToken` only helps at managed check-points. `Parallel.ForEach` with `ParallelOptions.CancellationToken` does cooperative cancel — in-flight iterations finish before workers stop.

**How to avoid:**
- Check `token.ThrowIfCancellationRequested()` (a) at the top of every recursion call, (b) inside the `while (FindNextFile)` loop every N entries (e.g. every 256), not only at directory boundaries.
- For `Parallel.ForEach`: pass `ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount }`. Accept that in-flight iterations complete — make each iteration a single directory, not a whole subtree, so granularity is small.
- Use `Task.Run` with `Parallel.ForEachAsync` (.NET 8) which plays better with async + cancellation than the classic `Parallel.ForEach`.
- On cancel, **dispose all `SafeFindHandle`s** before returning — otherwise the native enumeration continues holding locks.
- UI: disable the Cancel button and show "Cancelling…" immediately; expect up to ~2 seconds lag even with best practices.

**Warning signs:**
Cancel takes > 5 seconds after click. Task Manager shows CPU still at 100% after cancel. Log file keeps filling after the "Cancelling" line.

**Phase to address:**
Phase 2 (scanner) + Phase 3 (UI wiring). Test with a deliberately slow scan (e.g. on a network drive).

**Severity:** Critical

---

### Pitfall 7: `UnauthorizedAccessException` flood tanks scan performance

**What goes wrong:**
Even as administrator, a scan hits thousands of `UnauthorizedAccessException` on `System Volume Information`, `Config.Msi`, `Recycle.Bin`, per-user `AppData` of other accounts with deny ACLs, EFS-encrypted folders, etc. Each exception allocates a stack trace (~1 KB), logs to a file, and can slow scan by 10–100× (a `WinDirStat` scan of `C:` can take 10 minutes instead of 2 when the log file writes synchronously on every exception).

**Why it happens:**
Managed exceptions are expensive. `try { FindFirstFile(...) } catch (UnauthorizedAccessException)` at every directory costs ~100 μs minimum per throw. With P/Invoke specifically, the managed BCL wraps native errors into exceptions, but raw P/Invoke returning `INVALID_HANDLE_VALUE` + `GetLastError() == ERROR_ACCESS_DENIED` is free.

**How to avoid:**
- Using raw P/Invoke: check `FindFirstFileEx` returns and `Marshal.GetLastWin32Error()` — `ERROR_ACCESS_DENIED` (5), `ERROR_PATH_NOT_FOUND` (3), `ERROR_FILE_NOT_FOUND` (2) — and silently skip. **No exception allocated, no stack trace**.
- If you must use the managed BCL anywhere, wrap in `try { ... } catch (UnauthorizedAccessException) { /* no logging in hot path */ }` and accumulate errors in a `ConcurrentBag<(string Path, int ErrorCode)>` flushed **after** scan completes.
- Do **not** write to the log file from inside the scan loop — use an in-memory buffer and flush once at the end. Or use a channel-based async logger.
- Maintain a blacklist of always-inaccessible paths to pre-skip: `System Volume Information`, `$Recycle.Bin`, `Config.Msi`, `$WinREAgent`, hidden `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, `DumpStack.log.tmp`.

**Warning signs:**
Scan takes > 3× the expected time. CPU dominated by `clr.dll!RaiseException`. Log file > 100 MB from a single scan.

**Phase to address:**
Phase 2 (scanner) + Phase 7 (logging). Benchmark `C:\Windows` scan with and without exception-free path.

**Severity:** Critical

---

### Pitfall 8: Fuzzy matching false positives (AppData orphan detection)

**What goes wrong:**
Levenshtein threshold 0.7 matches "Adobe" folder against both "Adobe Reader" and "Adobe Photoshop" installed entries — the AppData folder is attributed to the wrong program, size aggregation is wrong, and "orphan" detection misses real orphans ("Acrobat" AppData folder when "Adobe Acrobat Reader DC" is installed is a true orphan or not depending on internal paths). Worse: "Microsoft" matches ~50 installed programs.

**Why it happens:**
Pure string-similarity scoring ignores semantic anchors. Common vendor prefixes ("Microsoft", "Adobe", "Google", "JetBrains") create O(N²) near-matches. Publisher names in the registry are wildly inconsistent: `"Microsoft Corporation"` vs `"Microsoft Corp."` vs `"© Microsoft"` vs blank. Folder names in `%AppData%` are chosen by the app developer, not the installer — rarely match the `DisplayName`.

**How to avoid:**
- **Two-stage match**, not a single Levenshtein score:
  1. Exact/prefix match on `InstallLocation`'s leaf folder first (strongest signal — if the folder name is literally equal to / parent of the AppData folder path, match with confidence 1.0).
  2. Token-based fuzzy (Jaccard on tokenized `Publisher + DisplayName`) only if step 1 fails, with minimum **token overlap ≥ 2 tokens** AND best-score margin ≥ 0.15 above second-best (reject ambiguous matches).
- Keep a curated alias table for known vendor variants (`"Microsoft Corporation"` = `"Microsoft Corp."` = `"Microsoft"`).
- **When ambiguous** (two scores within 0.1 of each other), classify as `AmbiguousMatch` and surface it in UI rather than guessing.
- Test corpus: build a fixture of 100 real registry+AppData pairings from a real Windows machine and run regression tests on it.
- Expose the match reasoning in the UI tooltip ("Matched to Adobe Reader via folder name `%AppData%\Adobe\Reader`").

**Warning signs:**
Orphan count seems too low on a system with lots of installed apps (under-reporting). User complains "this isn't an orphan, it belongs to X". Two distinct AppData folders attribute to the same installed program.

**Phase to address:**
Phase 5 (orphan/remnant detection). Requires test fixtures from a real system.

**Severity:** Critical

---

## High Severity Pitfalls

### Pitfall 9: Registry redirection (WOW6432Node) misses half the installed programs

**What goes wrong:**
Scanner reads `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` and reports 80 installed programs — user has 160. The 32-bit entries under `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` are missed entirely. On a 64-bit system most third-party apps (Office, Notepad++, 7-Zip, older installers) live in WOW6432Node.

**Why it happens:**
A 64-bit process (DiskScout will be win-x64) reading `HKLM\Software\...` sees the 64-bit view only. WOW6432Node is a physically separate key only visible by opening the registry with `RegistryView.Registry32`.

**How to avoid:**
- Explicitly enumerate **all four** uninstall locations:
  - `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` (64-bit view: `RegistryView.Registry64`)
  - `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` (32-bit view: `RegistryView.Registry32`) — this is WOW6432Node under the hood
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` (per-user 64-bit)
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` (per-user 32-bit, rare but exists)
- Use `RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)` etc. rather than the default `Registry.LocalMachine` (which follows the process bitness).
- De-duplicate by `(DisplayName, DisplayVersion, Publisher)` since the same entry sometimes appears in both views.
- Also scan `HKLM\Software\Classes\Installer\Products` (MSI product codes) if you want to cross-reference MSI-installed apps that may lack clean Uninstall entries.

**Warning signs:**
Installed-programs count noticeably lower than "Programs and Features" Control Panel. 7-Zip, Notepad++, VLC missing. All programs are UWP/64-bit (suspicious).

**Phase to address:**
Phase 3 (registry service). Verify against `Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*, HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*` as ground truth.

**Severity:** High

Sources: [Microsoft — WOW6432Node registry key](https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/wow6432node-registry-key-present-32-bit-machine), [Microsoft — Uninstall Registry Key](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key).

---

### Pitfall 10: Orphan Uninstall entries (registry keys point to deleted paths)

**What goes wrong:**
Registry says "Adobe Acrobat X is installed at `C:\Program Files (x86)\Adobe\Acrobat X`" — but the folder is gone (user deleted manually, migration artifact, failed uninstall). DiskScout reports this as "installed program, size 0 B" and never flags the dangling `InstallLocation` as a remnant in the other direction.

**Why it happens:**
Uninstallers don't always clean their own Uninstall key, and users sometimes delete program folders manually. Some registry entries are pure metadata (`SystemComponent = 1`, IE update entries) and have no `InstallLocation` at all.

**How to avoid:**
- For every `Uninstall` entry, validate: `InstallLocation` present AND `Directory.Exists(InstallLocation)`. If not, tag as `OrphanRegistryEntry` (surface in UI as a separate remnant category).
- Filter out pseudo-programs: `SystemComponent == 1`, `ParentKeyName` set, `ReleaseType in {"Security Update","Update","Hotfix"}`, `WindowsInstaller == 1` without DisplayName.
- Don't require `InstallLocation` — many MSI-installed apps omit it. Fall back to: `DisplayIcon` path (often points into the install folder), `UninstallString` path parsed with a quote-aware splitter, MSI's `ARPINSTALLLOCATION` property.
- When `InstallLocation` is missing but you have a folder candidate from `DisplayIcon`, derive the install location as the ancestor common to the icon path.

**Warning signs:**
Many installed programs showing "size: 0 B". Folders visibly present on disk but flagged as orphan. `UninstallString` referencing `msiexec.exe` with product codes only.

**Phase to address:**
Phase 3 (registry service) + Phase 5 (remnant detection cross-check).

**Severity:** High

---

### Pitfall 11: Admin elevation flow — UAC denied, silent crash or mysterious failures

**What goes wrong:**
User double-clicks `DiskScout.exe`, UAC prompt appears, user clicks "No". App either (a) launches anyway and then fails on first scan with cryptic "Access denied on `C:\Program Files`", (b) crashes silently, or (c) re-launches UAC in an infinite loop. Alternative: user runs from Explorer context "Run as administrator" but the working directory becomes `C:\Windows\System32`, breaking relative paths for the log file.

**Why it happens:**
`<requestedExecutionLevel level="requireAdministrator"/>` in `app.manifest` prevents launch entirely without admin — if the user clicks "No" on UAC, `ShellExecute` returns error `1223 ERROR_CANCELLED` and the process never starts. But if a developer uses `highestAvailable` or no manifest, the app runs non-elevated and fails silently. The working-directory trap is orthogonal: elevated processes get `System32` as CWD unless launched from Explorer with shift-right-click.

**How to avoid:**
- Use `level="requireAdministrator"` in manifest — clean fail is better than half-broken scan.
- Check elevation at startup regardless (`WindowsIdentity.GetCurrent()` + `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`); if not elevated, show a dialog explaining why and `Environment.Exit(1)` — don't limp on.
- **Never** use `Environment.CurrentDirectory` for log/scan file paths. Use `AppContext.BaseDirectory` (path to the `.exe`) or `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` (%LocalAppData%). The project's choice of `%LocalAppData%\DiskScout\` is correct.
- On UAC denial the launch simply fails — accept this, no clever workaround (re-prompting creates loops). Provide a README note for the user.
- Display current elevation state in the window title bar ("DiskScout [Admin]") so users confirm they got the elevated process.

**Warning signs:**
"Access denied" on `Program Files` despite "Run as admin". Log file appears in `C:\Windows\System32\diskscout.log`. Users reporting scans that are 50% incomplete.

**Phase to address:**
Phase 1 (bootstrap/manifest). Verify on a standard user account in a VM.

**Severity:** High

---

### Pitfall 12: Single-file publish + P/Invoke trimming breaks `DllImport` at runtime

**What goes wrong:**
App works in `Debug` / `dotnet run`. Published single-file `DiskScout.exe` launches but throws `DllNotFoundException` or `EntryPointNotFoundException` on first `FindFirstFileEx` call. Worse: it works on the developer's machine but fails on a clean VM because native dependencies weren't bundled. Or: `app.manifest` is not embedded, so the published exe runs without admin elevation and the UAC icon disappears.

**Why it happens:**
- `DllImport` IL stubs are generated at runtime and can be trimmed away if `PublishTrimmed=true` is combined with single-file (the classic IL trimming blind spot).
- `IncludeNativeLibrariesForSelfExtract=true` extracts native DLLs to a temp directory on each launch — if the user's AV is slow, first launch can take 10+ seconds.
- `ReadyToRun=true` with some P/Invoke signatures can fail.
- `app.manifest` must be referenced in `.csproj` via `<ApplicationManifest>app.manifest</ApplicationManifest>` — a bare file in the project does **nothing**.

**How to avoid:**
- Prefer `[LibraryImport]` (source-generated P/Invoke, .NET 7+) over `[DllImport]` — source gen is trim-safe and AOT-compatible. For DiskScout this applies to `FindFirstFileExW`, `FindNextFileW`, `FindClose`, `GetFileAttributesExW`, `GetDiskFreeSpaceExW`.
- If keeping `[DllImport]`: disable trimming (`PublishTrimmed=false`) OR add `[DynamicDependency]` / root descriptors. Trimming a 60 MB single-file down to 30 MB usually isn't worth the compatibility risk for an internal tool.
- Embed the manifest via `<ApplicationManifest>app.manifest</ApplicationManifest>` in `.csproj`, and verify with `mt.exe -inputresource:DiskScout.exe -out:extracted.manifest` after publish.
- Test the published `.exe` on a **clean Windows 11 VM** with no .NET SDK — this catches 80% of packaging bugs.
- Keep `PublishReadyToRun=false` until you've validated — it doubles binary size and sometimes breaks P/Invoke signatures silently.

**Warning signs:**
Works in VS debugger, fails as published exe. `DllNotFoundException: kernel32.dll` (impossible on Windows → trimming bug). UAC shield icon missing. Binary size < 50 MB (suspiciously trimmed).

**Phase to address:**
Phase 7 (packaging/publish). Never defer to release — validate on a clean machine early.

**Severity:** High

Sources: [Microsoft — Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [Microsoft — P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation), [Microsoft — Native interop best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices).

---

### Pitfall 13: Network drives mapped as local — scanning them destroys UX

**What goes wrong:**
User has a SMB share mapped as `Z:`. `GetDriveType("Z:\\")` returns `DRIVE_FIXED` in some mount scenarios (iSCSI, some VPN clients) and DiskScout happily scans it — taking 2 hours instead of 3 minutes, saturating the VPN, and sometimes hanging on disconnection.

**Why it happens:**
Users map network locations as local drives. Scanner code lists drives with `DriveInfo.GetDrives()` and filters by `DriveType.Fixed` but iSCSI/some tunnels report as Fixed. OneDrive/Dropbox placeholder files masquerade as regular files but block on cloud fetch when read.

**How to avoid:**
- Filter drives: include `DriveType.Fixed` only, and additionally check `DriveInfo.DriveFormat` (NTFS/ReFS are real local, anything else suspicious). Optionally check `GetVolumeInformation` for `FILE_REMOTE_DEVICE`.
- Let the user confirm the drive list in the UI before scanning; never scan all drives silently.
- For reparse points with cloud tags (OneDrive/Dropbox placeholders, `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`, `FILE_ATTRIBUTE_RECALL_ON_OPEN`), show size from `FindFirstFile` (which gives the stub size without triggering download) and tag the node "cloud placeholder — not downloaded".
- Add a **per-drive timeout** on scan start: if enumeration of the root takes > 5 seconds, prompt the user ("This drive looks slow, continue?").

**Warning signs:**
Scan time for `Z:` is 10× the C: scan. Network activity during scan. Files in OneDrive folder reporting size 0 or unexpectedly large downloads.

**Phase to address:**
Phase 2 (scanner) + Phase 4 (drive selection UI).

**Severity:** High

---

### Pitfall 14: Reserved/pagefile files skew totals and confuse users

**What goes wrong:**
`C:\pagefile.sys` (8–32 GB), `C:\hiberfil.sys` (RAM-sized), `C:\swapfile.sys` (~256 MB), `C:\DumpStack.log.tmp` appear at the root with huge sizes. Default scan includes them. UI reports "Windows folder: 48 GB" when the user expected 22 GB. Alternatively, scan silently skips them and the user's total disk usage doesn't match Explorer.

**Why it happens:**
These files are owned by the kernel. `FindFirstFile` sees them. Their sizes are legitimate disk usage. But users think of them as "system reserved", not "files".

**How to avoid:**
- **Include them but tag them**. Add a `IsSystemReservedFile` flag and group them in the tree under a synthetic "System Reserved" bucket at the root level (or color-code them). Show their size but explain what they are in a tooltip.
- Explicit list: `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, `DumpStack.log.tmp`, `$MFT`, `$LogFile`, `$Volume`, `$Bitmap`, `$Boot`, `$AttrDef`, `System Volume Information`, `$Recycle.Bin`.
- Never try to read their content — `FILE_ATTRIBUTE_SYSTEM | FILE_ATTRIBUTE_HIDDEN` → skip content, use `nFileSizeHigh/Low` from the find data directly.
- Reconcile total: `sum(all files)` should roughly equal `GetDiskFreeSpaceEx(Total) - GetDiskFreeSpaceEx(Free)`. Display the delta explicitly — users **will** notice if you hide it.

**Warning signs:**
User says "DiskScout is wrong, my disk isn't that full". Total disk used from scan differs by > 2 GB from `DriveInfo.TotalSize - DriveInfo.AvailableFreeSpace`.

**Phase to address:**
Phase 2 (scanner classification) + Phase 4 (tree UI grouping).

**Severity:** High

---

### Pitfall 15: JSON persistence — recursive tree blows the stack or produces 500 MB files

**What goes wrong:**
Scan produces a hierarchical tree with 1M+ nodes. `JsonSerializer.Serialize(rootNode)` with a default recursive class structure either (a) hits `StackOverflowException` at depth ~2000 (default thread stack 1 MB), (b) produces a 500 MB JSON file that takes 30 seconds to write and 60 seconds to load, or (c) succeeds but takes all user RAM.

**Why it happens:**
`System.Text.Json` uses recursion by default for hierarchical objects. Deep `node_modules`-style trees hit stack limits. JSON is verbose for tree-of-records data. Loading back via `JsonSerializer.Deserialize` re-allocates every node.

**How to avoid:**
- **Flatten on write**: store nodes as `List<FlatNode>` with `Id` and `ParentId`. Reconstruct the tree on load by building a dictionary and linking. Avoids recursion entirely, cuts file size by 30%, load is O(N).
- Set `JsonSerializerOptions.MaxDepth = 128` explicitly — raises clear exception instead of stack overflow.
- Use `System.Text.Json` source generation (`[JsonSerializable]` on a `JsonSerializerContext`) for 2–3× speedup and no reflection — also better for trimming.
- For files > 100 MB, consider `JsonSerializer.SerializeAsync` to a `GZipStream` — compressed JSON of a disk tree typically shrinks 8–12× because of repetitive folder name patterns.
- Version the schema: `schemaVersion: 1` at the top level. Migration code reads the version and upgrades old formats. Never assume current schema on load.
- Add a "max 1000 most recent scans, auto-prune" rule — otherwise `%LocalAppData%\DiskScout\scans\` becomes its own disk problem.

**Warning signs:**
`StackOverflowException` during save. Save takes > 5 seconds. File > 50 MB for a standard 500 GB scan. Memory usage during save > 2× the in-memory tree size.

**Phase to address:**
Phase 6 (persistence). Decide flat-vs-nested before writing the first serializer.

**Severity:** High

---

### Pitfall 16: Symlink cycles between volumes / mount-point volumes counted twice

**What goes wrong:**
A junction or mount point redirects from one volume to another (e.g. `C:\mnt\data` → `D:\`). DiskScout counts `D:` once when enumerated as a drive and again when walked through the junction — or vice versa, misses `D:` entirely because the user picked only `C:` but the junction inflated C's total.

**Why it happens:**
Mount points (`IO_REPARSE_TAG_MOUNT_POINT`) are Windows's answer to Unix bind mounts — they can point across volumes. The scanner needs to know volume boundaries.

**How to avoid:**
- For every directory entry, before recursing, check the volume serial number via `GetVolumeInformationByHandleW` (requires opening a handle) OR use `FILE_ID_INFO.VolumeSerialNumber` from `GetFileInformationByHandleEx(FileIdInfo)`. If the child's volume ≠ the scan's root volume, don't recurse (or recurse only if the user explicitly added that volume to the scan set).
- Store `(VolumeSerial, FileId)` pairs in a `HashSet` to detect "same file, reached twice via different junctions" — dedupe size.
- Document clearly in the UI: "This scan covers C: only. Junction `C:\mnt\data` is a link to `D:` which was not scanned."

**Warning signs:**
Total reported > `DriveInfo.TotalSize`. Two tree branches report the same file with different paths. "Ghost" entries at the root level with sizes matching another drive.

**Phase to address:**
Phase 2 (scanner core). Extension of Pitfall 1.

**Severity:** High

---

## Medium Severity Pitfalls

### Pitfall 17: 32-bit vs 64-bit file size (`nFileSizeHigh`/`nFileSizeLow`) truncation

**What goes wrong:**
Single large files > 4 GB (VM images, database files) are reported as their size modulo 4 GB because the code only reads `nFileSizeLow` and ignores `nFileSizeHigh`. A 50 GB VHDX shows as 2 GB. Total is off by tens of GB.

**How to avoid:**
`long size = ((long)data.nFileSizeHigh << 32) | (uint)data.nFileSizeLow;` — cast through `uint` to avoid sign extension of the low 32 bits. Always `long`, never `int`.

**Severity:** Medium

**Phase to address:** Phase 2

---

### Pitfall 18: `GetFileAttributesEx` on 8.3 short names / case sensitivity

**What goes wrong:**
The scanner returns the 8.3 short name (`PROGRA~1`) instead of `Program Files` because `cAlternateFileName` was read instead of `cFileName`, or path comparison fails because NTFS is case-insensitive but the comparison code uses `StringComparison.Ordinal`.

**How to avoid:**
Always read `WIN32_FIND_DATAW.cFileName`. Use `StringComparison.OrdinalIgnoreCase` for all path comparisons. When comparing full paths, `Path.GetFullPath` both first to canonicalize.

**Severity:** Medium

**Phase to address:** Phase 2

---

### Pitfall 19: Per-user HKCU scan misses other users' installed programs

**What goes wrong:**
User runs DiskScout as admin. HKCU reads point to the **admin's** HKCU, not the target user's. If the machine has multiple accounts, per-user installs for those accounts are invisible.

**How to avoid:**
- Enumerate user hives: `HKU\<SID>` for every loaded user hive. Use `Registry.Users`.
- For unloaded hives, open the user's `ntuser.dat` directly with `RegLoadKey` — but this is rarely worth it for a personal tool.
- Document the limitation: DiskScout shows admin+current-user installs. Don't silently pretend to scan all users.

**Severity:** Medium

**Phase to address:** Phase 3

---

### Pitfall 20: Exception logging floods the log file during scan

**What goes wrong:**
Every `UnauthorizedAccessException` / `PathTooLongException` / `IOException` is logged with full stack trace. 50k errors × 2 KB each = 100 MB log file. The 5 MB rotation rolls 20 times in a single scan, losing earlier context.

**How to avoid:**
- Log exceptions as error-code + path only in the hot path, not full stack traces.
- Use a `Dictionary<int, int>` error-code → count and log aggregate at the end ("403 ERROR_ACCESS_DENIED events: 1247, most recent at `C:\System Volume Information`").
- Rotate on scan boundaries, not on size, so you always keep the last complete scan's log.

**Severity:** Medium

**Phase to address:** Phase 7

---

### Pitfall 21: UI progress updates at 100k/sec overwhelm dispatcher

**What goes wrong:**
`IProgress<ScanProgress>` callback fires on every file, producing 100k-200k UI updates/sec. `Dispatcher.Invoke` queue explodes, UI freezes, memory balloons, scan throughput drops 5×.

**How to avoid:**
- Throttle progress reporting: update on time boundary (every 100–250 ms via a timer) **or** on count boundary (every 10k files), whichever comes first. Never on every file.
- Use `System.Threading.Channels` with a bounded capacity (e.g. 16) — producers drop if consumer falls behind.
- `Progress<T>` implementation internally marshals to `SynchronizationContext` — it does not coalesce. Roll your own throttled version.

**Severity:** Medium

**Phase to address:** Phase 3

---

### Pitfall 22: Aggregation races (parallel size rollup)

**What goes wrong:**
`Parallel.ForEach` on top-level directories writes child sizes back to `parentNode.Size` with `parentNode.Size += childSize`. Without `Interlocked.Add` or a local accumulator, concurrent updates lose writes; total is wrong by random small amounts.

**How to avoid:**
- Each worker computes a complete subtree in isolation, returns a single `SubtreeResult` object, and the merge is done on a single thread after all workers complete.
- OR use `Interlocked.Add` on a `long` for cumulative counters.
- Never mutate shared node state from inside parallel loops without synchronization.

**Severity:** Medium

**Phase to address:** Phase 2

---

### Pitfall 23: Drag-drop / clipboard of tree items trigger file system access

**What goes wrong:**
User drags a tree row into Explorer to "see" the file. WPF's default `DataObject` triggers `FileDrop` which Explorer interprets as a move/copy operation — surprising users who just wanted to highlight. Or: clipboard copy of a `FolderNode` serializes the entire subtree through `BinaryFormatter` (obsolete in .NET 8, will throw).

**How to avoid:**
- Override drag behavior to only set text (path string), not `FileDrop`.
- Never use `BinaryFormatter` (removed/obsolete in .NET 8). For clipboard, use `DataFormats.Text` with the path.
- Explicitly disable drag in list/grid unless it's a feature ("open in Explorer" context menu instead).

**Severity:** Medium

**Phase to address:** Phase 4

---

### Pitfall 24: Comparing two scans (delta) — `ObservableCollection.Reset` vs `Remove+Add`

**What goes wrong:**
Delta view shows 50k changed files. Implemented as `collection.Clear(); foreach(item) collection.Add(item);` — fires 50k `CollectionChanged` events, UI freezes. Users think "compare is broken".

**How to avoid:**
- Build the delta off-thread into a `List<>`, then `ObservableCollection` → assign a new instance (swap semantics, single `CollectionChanged.Reset` event).
- Or batch-update with `CollectionView.DeferRefresh`.

**Severity:** Medium

**Phase to address:** Phase 8 (scan comparison)

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Use `Directory.EnumerateFiles` instead of P/Invoke for "v1" | 2 days faster to first scan | Project core value (fast scan) is broken; cannot hit 3-min target on 500 GB; rewrite forced | Only for a throw-away prototype; never ship |
| Skip reparse-point detection ("I'll add it later") | Scanner logic is simpler | Silent infinite loops, wrong totals, user-visible bugs, rework required in every hot path | Never |
| Embed JSON tree with nested `Children` (no flattening) | Simpler serialization | `StackOverflow` on deep trees, giant files, slow load | Until you observe a tree > 500k nodes (should refactor then) |
| Single `ObservableCollection<FolderNode>` mutated directly from scan | 1 day faster MVP | Cross-thread exceptions, UI freeze, unpredictable data loss | Never on the critical path |
| `[DllImport]` + `PublishTrimmed=true` | Smaller binary | `DllNotFoundException` in production only | Never combined |
| Levenshtein-only fuzzy match | Simpler code | Silent false positives on common vendor prefixes, wrong orphan detection | Never for production data |
| No per-file exception handling in P/Invoke loop | Faster MVP code | One access-denied kills the whole scan | Never |
| Log every exception with stack trace | Easy debugging | 100+ MB log files, log rotation churn | During early dev only; gate behind `#if DEBUG` before ship |
| `Dispatcher.Invoke` per-file UI update | Simplest progress code | UI freezes, scan 5× slower | Never |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `kernel32!FindFirstFileEx` | Check `== IntPtr.Zero` | `SafeFindHandle` + `IsInvalid` (handle is `-1` on failure) |
| `kernel32!GetLastError` | Call `Marshal.GetLastWin32Error` after **any** managed code runs post-P/Invoke | Capture immediately; decorate P/Invoke with `SetLastError=true`; in .NET 8+ prefer `LibraryImport(SetLastError=true)` |
| Registry 64/32 views | `Registry.LocalMachine.OpenSubKey(...)` in a 64-bit process | `RegistryKey.OpenBaseKey(LocalMachine, Registry32)` + `(LocalMachine, Registry64)`, scan both |
| `HKCU` cross-user | Assume current identity = user whose programs you want | Enumerate `HKEY_USERS\<SID>` loaded hives; document limitation |
| `DriveInfo.GetDrives()` | Trust `DriveType.Fixed` blindly | Cross-check `DriveFormat` + `GetDriveType` native + prompt user to confirm drive list |
| `Path.Combine` + P/Invoke | Assume `\\?\` prefix survives BCL helpers | Build `\\?\` paths manually as strings; treat them as opaque |
| Manifest file | Dropped `app.manifest` into project root and assume it's picked up | `<ApplicationManifest>app.manifest</ApplicationManifest>` in `.csproj` |
| `BinaryFormatter` | Using for clipboard / persistence | Removed/obsolete in .NET 8 — use `System.Text.Json` |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `Directory.EnumerateFiles` managed path | Scan > 15 min on 500 GB SSD | Switch to `FindFirstFileEx` + `LARGE_FETCH` | Any disk > 100 GB |
| Exceptions in P/Invoke hot path | Scan time 3–10× longer than expected | Use raw return codes, not exceptions | Starts at ~1k deny errors (so any `C:\` scan) |
| Non-virtualized `TreeView` | 30-sec freezes, 2+ GB RAM | `VirtualizationMode=Recycling` + hosted-in-ScrollViewer audit | 10k expanded items |
| `Auto` column width in virtualized `DataGrid` | Sluggish scroll, memory growth | Fixed or `*` widths only | 10k rows |
| Per-file `Dispatcher.Invoke` progress | UI freeze during scan | Throttle to ~4 Hz with timer or channel | Any scan > 10k files |
| `ObservableCollection.Clear() + Add(x) × N` | UI freeze on compare / filter | Rebuild list off-thread, swap collection | 1k+ items |
| Sort-by-column on `DataGrid` with `Auto` width | Freeze on every sort click | Sort on `ICollectionView`, fixed widths | 10k rows |
| JSON with nested tree recursion | StackOverflow, 500 MB files, slow load | Flatten to `List<FlatNode>` + `ParentId` | 500k+ nodes |
| Unbounded log writes in scan loop | Scan slowdown, disk churn | Aggregate errors, flush once | 50k+ errors (any `C:` scan) |
| Parallel aggregation with non-atomic `+=` | Wrong totals, flaky numbers | Subtree-local accumulation + single merge | Any multi-threaded scan |

---

## Security & Safety Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Leftover `File.Delete` / `Directory.Delete` anywhere in codebase | Product-killing bug: destructive tool masquerading as read-only | `grep -r "\.Delete\("` as a pre-commit / CI check — fail build if found outside test code |
| `RecycleBin.SendToRecycleBin` via `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` | Same — moves files without user consent | Ban the `Microsoft.VisualBasic.FileIO` namespace project-wide |
| Reading file content (not just metadata) for size | Triggers OneDrive/Dropbox download, permission prompt on EFS, AV scan | Always `GetFileAttributesEx` / `WIN32_FIND_DATA` metadata only; never `File.Open` |
| Logging full paths including user personal folders to a shared location | Accidental PII leak | Log to `%LocalAppData%` only (already scoped to current user) |
| Storing registry values including `UninstallString` with product keys | Some installers stash license keys in `UninstallString` | Redact or exclude from exports by default |
| `Process.Start("explorer.exe", userPath)` without validation | Command injection via crafted paths | Pass the path as an argument array, not concatenated; use `ProcessStartInfo` with `UseShellExecute=true` and validate `IsPathFullyQualified` |
| Admin-elevated process loading plugins / DLLs from user-writable folders | Privilege escalation vector | Single-file publish (no external DLLs); never load from `%AppData%` or `%Temp%` |
| Clipboard export of entire scan as JSON | Leaks all filenames system-wide | Export to file on user action only, never to clipboard automatically |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| "Scan all drives" default selection | Scans `Z:\` VPN share, freezes app | Default to system drive only; let user tick others explicitly |
| Silent skip of system reserved files | Total disk usage doesn't match Explorer; user loses trust | Show "System Reserved (pagefile.sys, hiberfil.sys, ...): 24 GB" as a first-class tree node |
| Progress bar that jumps / goes backward | User thinks tool is broken | Separate "directories enumerated" (exact) from "bytes scanned" (estimate); show both, never go backward on total |
| "Scan complete" with no summary | Users stare at tree, don't know what to do | Headline summary: "Scanned 847,392 files in 2m 17s — 418 GB used, 12 orphan folders (8.4 GB), 73 programs" |
| No way to see *why* a folder is flagged as orphan | Users don't trust the detection | Tooltip/side panel: "This folder matches no installed program. Closest match: 'Adobe' (score 0.41, below 0.70 threshold)." |
| "Delete" button anywhere in UI | Violates core read-only promise; users will misclick | Absolute ban; "Open in Explorer" + "Copy path" are the only actions |
| Columns default to 5-decimal byte sizes ("4194304 B") | Unreadable | Default to human-readable (`4.0 MiB`), keep a "Show exact bytes" toggle |
| Sort persistence across scans | User sorts by size, new scan reverts to name | Remember last sort per-view in settings |
| No indication of scan age | User compares old scan thinking it's fresh | Show scan timestamp prominently in header |
| Cancel button grays instantly but scan keeps going 5s | User clicks 5 more times, thinks app is frozen | Immediate "Cancelling…" label + disabled button; small spinner until actually cancelled |
| Error spew in a toast / dialog during scan | User dismisses 100 dialogs | Aggregate errors in a single "View scan warnings" button at the end |

---

## "Looks Done But Isn't" Checklist

Verify each of these before declaring a phase complete:

- [ ] **Scanner core:** Feed a directory with a self-referential junction → completes without infinite loop (verify with `mklink /J C:\tmp\loop C:\tmp`)
- [ ] **Scanner core:** Path > 260 chars handled — test with a 300-char synthetic path (`mkdir` with repeated subdirs)
- [ ] **Scanner core:** `FindClose` called on every path including exception/cancel — check handle count in Task Manager before/after scan (should return to baseline)
- [ ] **Scanner core:** Files > 4 GB return correct size (test with a VM image or `fsutil file createnew bigfile.bin 5368709120`)
- [ ] **Scanner core:** Cancellation completes in < 2 seconds on any scan
- [ ] **Scanner core:** Scan total = `DriveInfo.TotalSize - DriveInfo.AvailableFreeSpace` within 5% after accounting for system-reserved files
- [ ] **Registry service:** Both `WOW6432Node` and 64-bit entries enumerated — cross-check count against Control Panel Programs
- [ ] **Registry service:** Orphan registry entries (`InstallLocation` points to deleted path) flagged
- [ ] **Registry service:** Pseudo-entries (`SystemComponent`, Updates, Hotfixes) filtered
- [ ] **Orphan detection:** Regression test fixture of 100 real AppData↔registry pairs — false positive rate < 5%, false negative rate < 10%
- [ ] **Orphan detection:** Ambiguous matches surfaced to user instead of auto-resolved
- [ ] **UI TreeView:** Expand a 10k-children node in < 500 ms; RAM growth < 100 MB
- [ ] **UI DataGrid:** Sort a 100k-row grid in < 1 s; RAM stable
- [ ] **UI TreeView:** `VirtualizingStackPanel.IsVirtualizing=True` verified in Live Visual Tree, not just XAML
- [ ] **UI progress:** `IProgress<T>` fires at ≤ 10 Hz max, no per-file callbacks
- [ ] **Persistence:** Save a scan of 1M nodes — file < 50 MB, save < 5 s, load < 10 s, no `StackOverflow`
- [ ] **Persistence:** Load an older scan (simulate by editing `schemaVersion`) — migration code runs, no crash
- [ ] **Admin elevation:** UAC prompt appears on launch; denial exits cleanly; title bar shows "[Admin]"
- [ ] **Admin elevation:** On a standard (non-admin) user account, launching without accepting UAC shows a clear error dialog, not silent failure
- [ ] **Publishing:** Single-file `.exe` launched on clean Windows 11 VM (no .NET SDK) — runs, scans, exits clean
- [ ] **Publishing:** Published `.exe` has embedded manifest (`mt.exe` to verify) with `requireAdministrator` + `longPathAware`
- [ ] **Publishing:** Binary size sanity check — ~70–90 MB expected for WPF single-file self-contained; < 50 MB means trimming removed something important
- [ ] **Read-only guarantee:** `grep -r "\.Delete(" src/` returns zero matches outside test helpers
- [ ] **Read-only guarantee:** `grep -rE "FileIO\.(DeleteFile|DeleteDirectory)"` returns zero matches
- [ ] **Logging:** Single scan of `C:\` produces < 5 MB log file
- [ ] **Export:** CSV and HTML exports open cleanly in Excel and browser respectively; Unicode filenames not mangled

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Reparse-point loop shipped (silent doubling) | MEDIUM | Add reparse detection, publish patch, invalidate any cached scans (bump schemaVersion) |
| `FindClose` leak discovered in production | MEDIUM | Convert to `SafeFindHandle`; users on long-running sessions should restart; ship hotfix |
| Virtualization broken (UI freeze on large folders) | LOW | Audit visual tree for `ScrollViewer`/`StackPanel` wrappers; set `VirtualizationMode=Recycling` |
| Registry 32/64 view missed | LOW | Add `RegistryView.Registry32` enumeration; re-run detection on load |
| Levenshtein false positives shipped | MEDIUM | Add two-stage matching + ambiguity flag; existing scans flagged as "matched with old heuristic" |
| JSON tree StackOverflow | HIGH | Migration code needed to rewrite old scans to flat format; bump `schemaVersion` to 2 |
| `DllImport` trimmed in published exe | LOW (if caught before release) | Switch to `LibraryImport`, republish; **HIGH** if shipped and users on trimmed version crash |
| Scan of network drive hangs | LOW | Add per-drive probe + timeout; prompt user |
| Admin elevation bypassed | LOW | Re-check manifest embedding; add runtime elevation check with clean exit |
| `ObservableCollection` cross-thread exception | LOW | Add `EnableCollectionSynchronization` + lock OR switch to swap pattern |
| `UnauthorizedAccessException` storm slowing scan | LOW | Replace try/catch with return-code check in hot path |

---

## Pitfall-to-Phase Mapping

Suggested phase sequencing based on which pitfalls each phase must prevent. Phase names are indicative — roadmap agent will finalize.

| Phase | Focus | Prevents |
|-------|-------|----------|
| Phase 1 — Bootstrap & manifest | .NET 8 WPF skeleton, `app.manifest`, admin elevation, log file path, MVVM scaffold | #11 (admin/UAC), #12 (manifest embedding), #20 (log location) |
| Phase 2 — P/Invoke scanner core (single-threaded) | `LibraryImport` signatures, `SafeFindHandle`, `\\?\` long paths, reparse detection, reserved-file classification, cancellation checkpoints, raw error codes (not exceptions), parallel aggregation | #1 #2 #3 #6 #7 #14 #16 #17 #18 #22 |
| Phase 3 — ViewModel & progress | `ObservableCollection` threading, throttled progress, channel-based updates | #5 #21 |
| Phase 4 — Tree & grid UI | Virtualization (TreeView + DataGrid), lazy-loading children, drive selection UI, column widths, cancel button UX | #4 #13 #23 |
| Phase 5 — Registry service & remnant detection | Dual 32/64 views, HKCU, orphan entries, two-stage fuzzy match, test fixtures | #8 #9 #10 #19 |
| Phase 6 — Persistence (JSON versioned) | Flat node list, source-gen serializer, schema migration, pruning | #15 |
| Phase 7 — Packaging & publish | Single-file publish, trimming decision, clean-VM validation, manifest verification | #12 (complete) |
| Phase 8 — Scan compare & export | Delta view without cross-thread freeze, CSV/HTML export Unicode-safe | #24 |

Cross-cutting concerns (every phase): read-only guarantee grep check (`.Delete(`), clean-VM publish smoke test, exception-free P/Invoke hot path benchmark.

---

## Sources

- Microsoft Learn — [FindFirstFileExW function](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfileexw)
- Raymond Chen — [When should I use the FIND_FIRST_EX_LARGE_FETCH flag?](https://devblogs.microsoft.com/oldnewthing/20131024-00/?p=2843) (The Old New Thing)
- Alexander Riccio — [How fast are FindFirstFile/FindFirstFileEx, and CFileFind?](https://ariccio.com/2015/01/13/how-fast-are-findfirstfilefindfirstfileex-and-cfilefind-actually/)
- Sebastian Schöner — [Using NtQueryDirectoryFileEx instead of FindFirstFile](https://blog.s-schoener.com/2024-06-24-find-files-internals/)
- pinvoke.net — [FindFirstFileEx signatures](https://www.pinvoke.net/default.aspx/kernel32/FindFirstFileEx.html)
- Microsoft Learn — [Improve the Performance of a TreeView (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview)
- Microsoft Learn — [Optimize control performance (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- CodeMag — [XAML Anti-Patterns: Virtualization](https://codemag.com/Article/1407081/XAML-Anti-Patterns-Virtualization)
- Microsoft microsoft-ui-xaml #818 — [Significant performance issues with TreeView on large number of items](https://github.com/microsoft/microsoft-ui-xaml/issues/818)
- Microsoft Learn — [Create a single file for application deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- Microsoft Learn — [P/Invoke source generation (LibraryImport)](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)
- Microsoft Learn — [Native interoperability best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)
- Microsoft Learn — [WOW6432Node registry key](https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/wow6432node-registry-key-present-32-bit-machine)
- Microsoft Learn — [Windows Installer Uninstall Registry Key properties](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)
- Microsoft Docs — `BindingOperations.EnableCollectionSynchronization` and cross-thread collection updates (general WPF threading documentation)
- Domain experience — WinDirStat / TreeSize / WizTree known quirks (common-knowledge community bug patterns)

---
*Pitfalls research for: Windows WPF disk analyzer with P/Invoke filesystem scan, registry reading, administrator privileges, long-path handling*
*Researched: 2026-04-24*
