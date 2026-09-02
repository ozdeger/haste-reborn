Haste Reborn — Development Handoff
===

Everything a developer needs to pick this up cold: what the tool does, how it does it,
what is verified about Unity 6, what is deliberately unfinished, and the traps that have
already cost time.

Written against commit history through the Unity 6 revival. Facts labelled **measured**
were obtained by running Unity 6000.0.80f1 or 6000.3.17f1, not from documentation — Unity's
docs are stale in several of these areas.

---

Part 1 — What the tool actually does
===

Haste is a keyboard-driven search palette for the Unity editor. Press a shortcut, type a
few characters, and jump to a GameObject in the open scene, an asset in the project, or a
Unity menu item — then act on it.

1.1 The core interaction
---

1. `Ctrl/Cmd+Shift+K` (or `Window > Haste`) opens a borderless popup centred on screen.
2. The text field has focus immediately. Every keystroke re-runs the search.
3. Results appear ranked, each row showing a name on top and its full path underneath, with
   the characters that matched your query **bolded in place** inside the path.
4. Arrow keys move the highlight. Enter acts on the highlighted row. Escape dismisses.
5. Clicking outside dismisses it. The palette never docks and is never saved into the
   window layout.

1.2 Fuzzy matching — the actual product
---

The feature people care about is that you do not type names, you type acronyms.

- `mc` finds `Main Camera`
- `gce` finds `GameObject/Create Empty`
- `pc` finds `Assets/Scripts/PlayerController.cs`
- `.cs` finds every C# script (searching by extension is a supported idiom)

This works because Haste indexes each item's **word boundaries**. For
`Component/Physics 2D/Polygon Collider 2D` the boundary string is `cp2dpc2d`: the first
character, every capital that follows a non-capital, every letter or digit after
punctuation or a space, and every `.`. Runs of capitals contribute only their first letter,
so `ALLCAPS/lowercase` yields `al` rather than exploding into every character.

1.3 What each source contributes
---

| Source | Indexes | Acting on a result |
|---|---|---|
| Hierarchy | GameObjects in loaded scenes, including inactive ones | Focuses the Hierarchy, selects, and pings the object |
| Project | Files and folders under the project | Focuses the Project window, selects, and pings the asset |
| Menu Item | Unity menu paths plus any `[MenuItem]` from your own code or other packages | Executes the menu item |
| Layout | Saved window layouts | Switches layout |

Hierarchy rows are colour-coded exactly like Unity's own Hierarchy window: normal, prefab
blue, broken-prefab red, each dimmed when the object is inactive.

1.4 Selection behaviour, which is subtler than it looks
---

There are **two independent selection concepts**, and conflating them breaks the tool:

- **Highlight** — the single row the keyboard is on. Drives Enter and drives "soft
  selection", where merely scrolling through results temporarily selects the object in the
  editor so you can see it. Soft selection is switchable off in preferences, because it
  expands Hierarchy and Project folders as it goes.
- **Multi-selection** — an explicit set built with `Cmd/Ctrl+Click` or `Cmd/Ctrl+Enter`,
  shown as a count and a row of icons in the window's top-right, with a dot marker on each
  chosen row. Enter with a non-empty set selects the whole set.

On dismissal via Escape the previous editor selection is restored, so browsing results is
non-destructive.

1.5 Other behaviours worth knowing
---

- **Drag and drop out of the palette.** A result can be dragged straight into the Hierarchy
  or Project window, or onto an Inspector field.
- **Recency.** Recently chosen items are remembered and shown when the palette opens with
  an empty query. Each pick sets its score to 1.0; every subsequent pick decays all stored
  scores by 0.9 and drops anything under 0.1. Recency also multiplies search score by
  `1 + userScore/10`.
- **Ignore list.** Right-click a folder → `Haste > Ignore` to keep it out of results, with
  a reorderable list in preferences.
- **Rotating tips** in the footer, and an indexing indicator while sources are still being
  crawled.
- **Indexing is incremental in effect**: new items become searchable as they are discovered
  rather than after a blocking scan.

---

Part 2 — Architecture
===

2.1 The pipeline
---

```
Sources ──> Watchers ──> Index ──> Search ──> Results ──> Window
```

Everything is driven by one static god-object, `Haste`, which is `[InitializeOnLoad]` and
owns five singletons. It pumps a hand-rolled coroutine scheduler from
`EditorApplication.update` with a **16 ms per frame budget** (`Haste.MAX_ITER_TIME`), so
neither indexing nor searching can stall the editor.

2.2 File map
---

`Packages/com.hastereborn.haste/Editor/`

| File | Role |
|---|---|
| `Haste.cs` | Static entry point, singletons, the update pump, source registration |
| `HasteScheduler.cs` | Stoppable coroutine scheduler. `HasteSchedulerNode` can be cancelled mid-flight, which is how a keystroke cancels the previous search |
| `HasteIndex.cs` | `Dictionary<char, HashSet<HasteItem>>` bucketed by boundary character |
| `HasteSearch.cs` | Filter → Map → Sort, each stage yielding when it exceeds the frame budget |
| `HasteScoring.cs` | **The ranking algorithm.** 60 lines and the highest-value code in the repo |
| `HasteStringUtils.cs` | Boundary extraction, subsequence matching, weighted-subsequence highlight indices, path helpers |
| `HasteWatcher.cs`, `HasteWatcherManager.cs` | Per-source diffing re-crawlers emitting Created/Deleted |
| `Items/HasteItem.cs` | One indexed thing: path, lowercased forms, boundaries, bitset, extension, recency |
| `Sources/*.cs` | The four enumerators |
| `Results/*.cs` | Per-source rendering and the `Action()`/`Select()` behaviour |
| `GUI/*.cs` | IMGUI widgets, `IDisposable` layout wrappers, manual list virtualization |
| `HasteWindow.cs` | The palette window and its 4-state machine (Intro/Loading/Results/Empty) |
| `HasteStyles.cs`, `HastePalette.cs` | GUIStyle matrix and light/dark colour pairs |
| `HasteSettings.cs` | `EditorPrefs` wrapper keyed by a `HasteSetting` enum |
| `HastePreferences.cs` | The preferences page |
| `HasteRecommendations.cs` | Recency store, a `ScriptableSingleton` in `UserSettings/` |
| `HasteResources.cs` | Loads packaged assets by package-relative path |
| `HasteShortcut.cs` | How the palette is opened |

`Assets/Dev/` holds benchmarks and fixture generators and is **never shipped**.
`Assets/Testing/` and `Assets/Tutorial/` are fixture scenes, including `Big.unity` and
`VeryBig.unity` for perf work.

2.3 The scoring algorithm, in detail
---

Read `HasteScoring.Score` alongside this. Given a lowercased query:

```
boundaryMatchCount   = LCS(query, item.boundariesLower)
boundaryQueryRatio   = boundaryMatchCount / query.Length
boundaryUtilization  = boundaryMatchCount / boundaries.Length

score  = 40 * boundaryQueryRatio
score += 40 * boundaryUtilization
```

Then a single early-returning ladder adds exactly one bonus:

| Condition | Bonus |
|---|---|
| `nameLower == query` | +60 |
| `pathLower == query` | +50 |
| query ≥ 3 chars and `nameLower` starts with query | +40 |
| query ≥ 3 chars and `pathLower` starts with query | +30 |
| first character of `nameLower` matches | +20 |
| first character of `pathLower` matches | +10 |

The result is multiplied by `1 + userScore/10`.

Both boundary terms matter. `boundaryQueryRatio` rewards consuming your whole query;
`boundaryUtilization` rewards consuming the whole item. That second term is why
`Directional Light` scores a perfect 100 for `dl` — a two-boundary item fully matched by a
two-character query saturates both — while `Assets/Scripts/Player/PlayerMovement.cs` scores
only 51 for `mc`.

Ties break by score, then **shorter path first**, then `EditorUtility.NaturalCompare`.

2.4 Search performance design
---

Filtering is a three-stage funnel, cheapest first:

1. Length check — the item's path cannot be shorter than the query.
2. **Letter bitset** — `1 << c` per character, OR-ed. The shift wraps mod 32, so this is a
   lossy 32-bit signature, not a real letter set. `(itemBits & queryBits) == queryBits`
   rejects fast. Collisions are expected and harmless: `Mesh Collider` and
   `Cloth Renderer` share a bitset.
3. Real subsequence walk.

Sorting is an async in-place quicksort that falls back to `Array.Sort` under 1000 elements.

2.5 Highlighting
---

`GetWeightedSubsequence` is a small backtracking matcher that prefers boundary positions
over interior ones, so `mc` against `Component/Physics/Mesh Collider` bolds the `M` and `C`
of `Mesh Collider` rather than the first available `m` and `c`. Those indices feed
`BoldLabel`, which splices rich-text markup into the path string.

---

Part 3 — Verified Unity 6 facts
===

Do not re-derive these. Each was measured on the installed editors.

3.1 Method for checking an API
---

Unity's XML docs list obsolete and removed stubs, so they only prove a name was once
documented. Read assembly metadata instead, with the Mono.Cecil that ships in the editor:

```powershell
Add-Type -Path 'C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Data\Managed\Unity.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
  'C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Data\Managed\UnityEngine\UnityEditor.CoreModule.dll')
```

Check accessibility (`IsPublic` vs `IsAssembly`) and read `ObsoleteAttribute` —
`ConstructorArguments[1].Value == true` means it is a **compile error**, not a warning.
Cecil also reads method bodies, which is how several of the findings below were settled.

3.2 What is available
---

- **`EditorApplication.globalEventHandler`** — internal, present and identical in 6000.0 and
  6000.3. Unity's own `ShortcutIntegration` hooks it with `Delegate.Combine`, so it cannot
  be removed quietly. **Post-consumption**: Unity describes it as "events that were not
  handled by anyone".
- **`GUIUtility.beforeEventProcessed`** — internal `Action<EventType,KeyCode,EventModifiers>`
  in `UnityEngine.IMGUIModule`. **Pre-consumption**: invoked in `GUIUtility.ProcessEvent`
  after `CopyFromPtr` and before the dispatch. This is the correct hook for input detection.
- **`HierarchyProperty`** — **public** in Unity 6, though completely undocumented. The
  native walker Unity's own Hierarchy uses; gives name, depth, instanceID and `colorCode`
  with zero managed object loads, and `colorCode` is `{0 Normal, 1 Prefab, 2 BrokenPrefab}`.
- **`ObjectChangeEvents.changesPublished`** — precise incremental hierarchy deltas.
- **`Menu.GetMenuItems(path, includeSeparators, localized)`** — internal, returns 504–508
  clean menu paths in ~2.3 ms. `Menu.GetMenuItems("")` returns **0**; there is no root
  enumeration and no public root API.
- **`EditorApplication.ExecuteMenuItem`** — now backed by the native menu tree and works for
  built-ins, which removes the reason `HasteActions.cs` exists.
- **`Unsupported.GetSubmenus(root)`** — public fallback giving the same flattened path list.
- **`AssetDatabase.LoadAssetAtPath("Packages/<name>/...")`** — works for embedded and for
  read-only `Library/PackageCache` installs alike.

3.3 What is not available, and the traps
---

- **`ShortcutManager` can never express a modifier-only or double-tap binding.**
  `BindingValidator.s_InvalidKeyCodes` contains every modifier keycode. A malformed
  `[Shortcut]` **compiles cleanly**, registers the id with an **empty** binding, and only
  logs a discovery warning — so always assert on the binding, never on compilation.
- **`EditorStyles.textField` throws `NullReferenceException` under `-batchmode`, even
  without `-nographics` and with a real Direct3D 11 device.** Editor GUI styles need an
  interactive editor, not merely a graphics device. Anything gated behind an `EditorStyles`
  readiness check is unreachable headlessly.
- **`EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).font` is unassigned.** Reading it
  succeeds; using it throws `UnassignedReferenceException` from the native call.
- **`Font.RequestCharactersInTexture` has no callers left** in Unity's own editor
  assemblies. IMGUI text routes through `IMGUITextHandle` into TextCore, so glyph
  pre-warming warms nothing.
- **`ScriptableSingleton` does not auto-save.** Its hideFlags include `DontSaveInEditor` and
  `CreateAndLoad` re-reads from disk, so any mutation not followed by `Save(true)` is lost
  on the next domain reload. `Save` and `GetFilePath` are `protected`.
- **`Selection.activeInstanceID` / `instanceIDs`** are obsolete in 6000.3 in favour of
  `activeEntityId` / `entityIds`, **which do not exist in 6000.0**. Prefer
  `Selection.objects` plus `Selection.selectionChanged`, which are clean on both.
- **`package.json`'s `unity` field must be exactly `<major>.<minor>`.** `"6000.0.80f1"`
  makes the package fail to resolve entirely; the suffix belongs in `unityRelease`. The
  three URL fields are silently dropped unless absolute http/https.
- **`AsyncProgressBar` does not exist** in Unity 6. Use `UnityEditor.Progress`.
- **`UnityEditor.Delayer`**, Unity's own debouncer, is internal — write your own.
- **`Packages/` dominates asset counts**: 4836 of 4949 paths in a stock project. Indexing it
  unfiltered buries the user's own assets.
- **`CreateGameObjectHierarchy` coalesces.** Creating a root plus three children in one
  frame emits **one** event, for the root. Always treat the instanceId as a subtree root.
- **`ChangeGameObjectOrComponentProperties` is the only rename signal** and also fires on
  every transform tweak. Without a name-compare guard the index thrashes on every drag.
- **Unity rewrote its own palette from IMGUI to UI Toolkit inside the Unity 6 cycle.**
  2022.3 has `QuickSearch` with `OnGUI`; 6000.x has `SearchWindow` with `CreateGUI` and a
  plain public `ListView`. IMGUI is not deprecated — 79 of 151 built-in editor windows
  still use `OnGUI` — but this specific widget was migrated.

---

Part 4 — Behaviours that look like bugs
===

These are real, currently-shipping behaviours, pinned by tests so a rewrite cannot change
them silently. Two of them arguably *should* change; that is a product decision.

4.1 The first query character must begin a word
---

The index buckets only by boundary characters, so an item is never even scored unless the
query's **first** character starts a word somewhere in it.

- `ollider` and `ysics` return **nothing**, despite `Collider` and `Physics` being indexed.
- `amera` **does** match the camera assets, because `a` begins `Assets`.
- `mc` returns 5 results out of a 20-item corpus even though 14 score non-zero.

This is the single biggest recall limitation. The proposed fix is to drop the
boundary-first *filter* while keeping "first char is a boundary" as a scoring **boost**,
which preserves the acronym feel while losing no results.

4.2 Menu paths ending in `...` get a corrupted name
---

`GetFileNameWithoutExtension("Component/Add...")` returns `Add..` and
`GetExtension` returns `..`. Most Unity dialog menu items end in `...`, so a large fraction
of menu rows have a wrong display name and a bogus extension polluting extension search.

4.3 Other known-but-unfixed items
---

- `HasteIndex.Remove` decrements `Count` unconditionally, even for an item that was never
  present.
- `Haste.Update`'s frame budget captures `start` once outside the loop while accumulating
  elapsed time each iteration, producing a triangular sum — so the 16 ms budget is
  exhausted early.
- `EditorApplication.LockReloadAssemblies` is called in `Open()` but unlocked only in a
  `new`-shadowed `Close()`, so any close path that misses the shadow leaks a reload lock.
- `HasteStyles.WaitUntilReady()` busy-spins on `EditorStyles` forever in batch mode.

---

Part 5 — Current state and what is next
===

5.1 Done
---

- **Pro/free split removed.** `IS_HASTE_PRO` gated menu search, recency and menu actions
  behind a "Pro" edition and nothing in the repo ever defined it, so building from source
  silently produced a crippled build. All 19 sites stripped.
- **Dead code deleted**: `UnityTestTools` (245 files, and the only compile errors in the
  repo), the update checker that polled a dead domain over obsolete `WWW`, `JSON.cs`, the
  Asset Store upsell, and the `System.CodeDom` DLL export pipeline.
- **Assembly isolation.** Three Editor-only assemblies. Nothing lands in
  `Assembly-CSharp-Editor` any more, which is what previously let a broken neighbour take
  Haste down with it.
- **Embedded UPM package** at `Packages/com.hastereborn.haste`, installable by git URL.
- **Characterization suite** — 64 tests pinning ranking, scoring, highlighting, packaging
  and activation.
- **Shortcut moved off Ctrl/Cmd+K** onto `[Shortcut]`, because Unity 6 owns that chord.
- **Three runtime crashes fixed** that all compiled cleanly: the `\_` regex, the font
  pre-cache, and the resource-folder scan.

5.2 Not done
---

Phases 2 and 4–7 of the plan, in order: the obsolete-API burn-down; the live-menu rewrite
that deletes 724 hardcoded menu paths and ~46 of 51 `HasteActions` fallbacks; event-driven
incremental indexing with stable identity keys; the search-core rewrite; the UI Toolkit
palette; and settings consolidation.

The UI decision is settled — **UI Toolkit**, added alongside the working IMGUI window and
made default only once its own tests pass. See `Documentation~/activation-design.md` for the
double-tap-Shift design, which is designed but not implemented.

5.3 Open product decisions
---

1. Whether the recall fix (4.1) drops boundary-first entirely or keeps it as a boost.
2. Whether to index only mutable packages, all packages, or none.
3. Whether the movable-window mode survives, given the centring bug that motivated it is
   being fixed.
4. Ignore-list scope (team-shared vs per-user) and semantics (literal prefixes vs globs).
5. Whether `Image Assets/` — the old Asset Store marketing artwork — stays in the repo.

---

Part 6 — Working on this
===

6.1 Verification commands
---

Compile check, run against **both** editors:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Unity.exe" -batchmode -quit -nographics -projectPath <proj> -logFile <log>
```

EditMode tests — note `-runTests` must **not** be combined with `-quit`:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Unity.exe" -batchmode -nographics -projectPath <proj> -runTests -testPlatform EditMode -testResults <xml> -logFile <log>
```

Gate on the results XML showing `failed="0"` **and** `total > 0`, not on the exit code
alone: a run-level failure can still exit 0. Exit 2 means test failures.

Work against a **copy** of the project, not the repo, so a failed run cannot leave the
working tree upgraded or locked. Each editor version needs its own copy, because `Library/`
is version-specific.

6.2 What headless verification cannot tell you
---

This has already cost real bugs. A green compile and a passing `-nographics` suite do not
exercise:

- editor GUI, styles, fonts or GUISkin (`EditorStyles` throws in batch mode regardless of
  graphics device)
- anything downstream of an `EditorStyles` readiness gate
- real keyboard or mouse input, on any platform
- `[InitializeOnLoad]` → scheduler startup chains, since `-quit` exits before
  `EditorApplication.update` pumps them

For those, open the editor. Pin the observable *precondition* in a test instead of the
crash, add a reflection guard so a deleted landmine stays deleted, and then say plainly
that confirmation needs a human.

6.3 macOS
---

macOS is a hard requirement and there is no Mac on the development machine, so mac
behaviour is **review-only**. Consequences:

- Branch on `Application.platform` at **runtime**, never `#if UNITY_EDITOR_OSX` — a
  Windows-built editor assembly bakes in the compiling editor's symbol.
- Use `ShortcutModifiers.Action`, which resolves to Cmd on macOS and Ctrl elsewhere at
  runtime. Never `ShortcutModifiers.Control`, which means the literal Ctrl key even on a Mac.
- Use `AssetDatabase`'s already-`/`-separated project-relative paths rather than string
  surgery on `Application.dataPath`.
- Make every path and prefix comparison **`Ordinal`**. The development machine runs
  `tr-TR`, where `"I".ToLower()` is dotless `ı` and culture-sensitive prefix matching
  genuinely diverges from ordinal.
- Two things are genuinely unknown on macOS: whether a borderless popup takes keyboard
  focus without an activating click, and whether a bare Shift press is delivered as a
  KeyDown at all (macOS reports modifier changes as `flagsChanged`).

6.4 Ground rules that earned their place
---

- **Never trust a green compile as proof a feature works.** Three shipped crashes compiled
  cleanly.
- **Generate test expectations by running the code**, not by deriving them. Deriving them by
  hand produced two wrong values that a measured run caught.
- **Verify against assembly metadata, not documentation**, and say how each claim was
  checked.
- **Keep the tool working at every commit.** No phase may leave the palette broken.
- `HasteScoring` and `HasteStringUtils` are the product. Any change there must keep the
  golden tables passing, or re-baseline them as a visible, deliberate diff.
