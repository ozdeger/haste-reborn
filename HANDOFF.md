Haste Reborn — Development Handoff
===

Everything a developer needs to pick this up cold: what the tool does, how it does it,
what is verified about Unity 6, what is deliberately unfinished, and the traps that have
already cost time.

Written against commit history through the Unity 6 revival. Facts labelled **measured**
were obtained by running Unity 6000.0.80f1 or 6000.3.17f1, not from documentation — Unity's
docs are stale in several of these areas.

**Read 6.3 first if you are picking this up.** The document was originally written on a
Windows machine with no Mac, and that shaped several of its conclusions. Development has
since moved to macOS with 6000.3.17f1, where the suite is green — but **6000.0.80f1 is not
installed there**, so the dual-editor verification the rest of this document assumes is
currently single-editor. Anything below that says "verified on both" was verified by
someone else, on a machine this one is not.

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
- `ollider` finds `Mesh Collider` — interior substrings work too, ranked beneath the
  acronym matches (see 4.1)

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

Every result is multiplied by a weight after matching, so whole categories can be pushed
down without being hidden (`HasteWeights`, per-user, in EditorPrefs). Menu items are the
exception: they are weighted by **menu root** rather than by kind (`HasteMenuWeights`),
because Unity's ~529 stock commands and a project's own `Dev Tools/` menu arrive through
the same source and want opposite treatment. The split is drawn at
`HasteMenuItemSource.BuiltinRoots` — the editor's own menu bar starts at 0.7, anything a
`[MenuItem]` invented starts at 1.0 and gets a slider as soon as it is discovered.

The scope token for menu items is **`menu`** (`t:menu `, `menu:`, or the `>` sigil), and
`HasteKind.Menu` is the kind behind it. `command` and `cmd` still parse — they were the
name until this rename — but they are not advertised and they present as "menu", so the
chip shows one word whichever was typed.

1.4 Selection behaviour, which is subtler than it looks
---

There are **two independent selection concepts**, and conflating them breaks the tool:

- **Highlight** — the single row the keyboard is on. Drives Enter and drives "soft
  selection", where merely scrolling through results temporarily selects the object in the
  editor so you can see it. **Off by default**, and switchable on in preferences: it
  expands Hierarchy and Project folders as it goes, and unlike the selection itself that
  rearrangement is not undone on Escape.
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
| `HasteIndex.cs` | `Dictionary<char, HashSet<HasteItem>>` bucketed by **path character**, plus an authoritative item set. Bucketing by *boundary* character is what caused 4.1 |
| `HasteSearch.cs` | Filter → Map → Sort, each stage yielding when it exceeds the frame budget |
| `HasteScoring.cs` | **The ranking algorithm.** 60 lines and the highest-value code in the repo |
| `HasteStringUtils.cs` | Boundary extraction, subsequence matching, weighted-subsequence highlight indices, path helpers |
| `HasteWatcher.cs`, `HasteWatcherManager.cs` | Per-source diffing re-crawlers emitting Created/Deleted |
| `Items/HasteItem.cs` | One indexed thing: path, lowercased forms, boundaries, bitset, extension, recency |
| `Sources/*.cs` | The four enumerators. `HasteMenuItemSource` reads the editor's live menu tree; it used to ship hardcoded menu tables |
| `Results/*.cs` | Per-source rendering and the `Action()`/`Select()` behaviour |
| `GUI/*.cs` | IMGUI widgets, `IDisposable` layout wrappers, manual list virtualization |
| `UI/HasteSpotlightWindow.cs` | The palette. UI Toolkit; replaced the IMGUI `HasteWindow` |
| `InternalResources/UI/HasteSpotlight.uss` | All palette styling, values taken from the design |
| `HasteKinds.cs` | Presentation taxonomy for row badges and scope tokens |
| `HasteItemActions.cs` | What the actions pane offers, per kind |
| `HasteKeyMap.cs` | The keyboard map, as a pure function so it can be tested |
| `HasteWeights.cs` | Per-kind score multipliers, per-user, applied in `HasteSearch.Map` |
| `HasteIgnoreRules.cs` | The shipped ignore list and the matcher, pure and testable |
| `HasteIgnorePaths.cs` | The project's shared ignore list, committed in `ProjectSettings/` |
| `HasteDoubleTapShiftGesture.cs` | Double-tap-Shift recognition, pure and clock-injected |
| `HasteDoubleTapShift.cs` | The `beforeEventProcessed` hook that feeds it |
| `HasteDisplay.cs` | Where the palette opens |
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

if query[0] does not begin a word in the item:
  score *= INTERIOR_START_DAMPING   # 0.5 -- see 4.1
```

Then a single early-returning ladder adds exactly one bonus. The ladder is **never**
damped, only the boundary terms above it are:

| Condition | Bonus |
|---|---|
| `nameLower == query` | +60 |
| `pathLower == query` | +50 |
| query ≥ 3 chars and `nameLower` starts with query | +40 |
| query ≥ 3 chars and `pathLower` starts with query | +30 |
| query ≥ 3 chars and `nameLower` contains query | +25 |
| first character of `nameLower` matches | +20 |
| query ≥ 3 chars and `pathLower` contains query | +15 |
| first character of `pathLower` matches | +10 |

The result is multiplied by `1 + userScore/10`, and then in `HasteSearch.Map` by
`HasteWeights.For(item)` — a per-kind multiplier that is a *preference*, not a measure of
the match. Keeping it out of `HasteScoring` is deliberate: the ladder answers "how well
does this match", the weight answers "how much is this kind wanted at all", and only the
first belongs in the golden tables. Defaults demote hierarchy objects to 0.5 and menu
items to 0.7; project assets stay at 1.

A result scoring exactly 0 is discarded by `HasteSearch.Map` rather than shown — which
means a weight of 0 removes a kind from results entirely. That is documented in the
preferences page rather than prevented.

Every comparison in the ladder is `Ordinal`, per the rule in 6.3 — the two prefix rungs
used `InvariantCulture` until the recall fix.

Both boundary terms matter. `boundaryQueryRatio` rewards consuming your whole query;
`boundaryUtilization` rewards consuming the whole item. That second term is why
`Directional Light` scores a perfect 100 for `dl` — a two-boundary item fully matched by a
two-character query saturates both — while `Assets/Scripts/Player/PlayerMovement.cs` scores
only 51 for `mc`.

Ties break by score, then **shorter path first**, then `EditorUtility.NaturalCompare`.

2.4 Search performance design
---

`Filter` looks up **exactly one bucket**, keyed by the query's first character, and never
consults another. That single fact is why the index's choice of key is a correctness
concern and not just a performance one — see 4.1.

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
- **Unity 6 moved the editor windows under `Window/General/`** — `Window/General/Project`,
  `Window/General/Hierarchy`, `Window/General/Inspector`, `Window/General/Console`. The
  Unity 5 paths are gone, and `ExecuteMenuItem` on a path that does not exist **does not
  throw**: it logs a native error and returns false. Haste focused nothing and printed a
  stack trace for years' worth of that. `HasteEditorWindows` resolves these from
  `Unsupported.GetSubmenus("Window")` by last path segment instead of hardcoding them,
  for the same reason `HasteMenuItemSource` stopped shipping a menu table.
- **A prefab ASSET reports `activeInHierarchy == false`.** Not because it is disabled —
  because it is not in a scene at all. Combined with `AssetDatabase.LoadMainAssetAtPath`
  returning a prefab's **root GameObject**, any code that decides "is this a hierarchy
  object?" by testing `obj as GameObject` will treat every prefab in the Project as an
  inactive scene object. In the palette that greyed out every prefab row. Decide from the
  item's SOURCE instead; `HasteKinds.UsesHierarchyTint` is that check.
- **Human double-tap timings, measured** (154 Shift transitions, one session, macOS
  6000.3.17f1): tap hold median 82 ms, p90 **117 ms**; deliberate holds 1300–1600 ms; gap
  between taps median 93 ms. Useful because both constants in `activation-design.md` were
  guesses and both were too tight — a 120 ms tap limit discarded 6% of genuine taps, and a
  "3 fires in 10 s" runaway breaker fired twice on someone simply testing the feature.
- **A bare modifier press reaches `beforeEventProcessed` only as modifier BITS, not as a
  key event — at least on macOS.** Measured on 6000.3.17f1: tapping Shift produces no
  `KeyDown`/`KeyUp` whatsoever. `EditorApplication.modifierKeysChanged` fires, and one
  millisecond later a `Repaint` arrives carrying `mods=Shift (was None)`. So a
  modifier-only gesture has to be built on transitions of the modifier bits observed across
  *any* event type, which is what `HasteDoubleTapShiftGesture` does. The cost is that the
  bits cannot distinguish `LeftShift` from `RightShift`.
- **`Menu.GetMenuItems(path, includeSeparators, localized)`** — internal, returns
  `ScriptingMenuItem[]` (`path`, `isSeparator`, `priority`). **Now in use**; measured on
  6000.3.17f1/macOS it returns 529 clean paths across every root in **0.98 ms**, works
  under `-batchmode -nographics`, and includes items declared by `[MenuItem]` attributes
  (Haste's own `Window/Haste` shows up in it). Of those paths, none ends in `/`, none
  carries a shortcut suffix, and none is duplicated.
  `Menu.GetMenuItems("")` returns **0**; there is no root enumeration and no public root
  API — so roots must be named, and the ones packages invent (`Services`) are found by
  scanning `[MenuItem]` attributes for root names only.
- **`EditorApplication.ExecuteMenuItem`** — now backed by the native menu tree and works for
  built-ins, which removes the reason `HasteActions.cs` exists.
- **`Unsupported.GetSubmenus(root)`** — public, confirmed by call: returns the same 172
  paths for `Component` that the internal API does. It is the degradation path in
  `HasteMenuItemSource`, and the independent oracle the menu tests assert against.
  `GetSubmenusIncludingSeparators` returns 224 for the same root (parent nodes and
  separators), and `GetSubmenusCommands` returns command ids, not paths.
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
- **`ListView` puts its item USS classes on the element `makeItem` returns**, in the
  default reorder mode — not on a wrapper around it. So `unity-collection-view__item` and
  `…--selected` land on your own row element. Two things follow, and both cost time: a
  selector written as `…--selected > .my-row` matches nothing, and any rule you write
  against `.haste-list .unity-collection-view__item` is **(0,2,0)** and will out-specify a
  single-class modifier like `.my-row--highlighted` at **(0,1,0)**. Clearing Unity's own
  selection paint that way silently disables your highlight, and the only visible symptom
  is that *descendant* rules still work — so child elements look selected while the row
  never does.
- **USS has no `cubic-bezier()`** — named easings only (`ease`, `ease-in-out`,
  `ease-out-cubic`, …). This one is worth knowing because of HOW it fails: an unsupported
  value is **not** an import error. The importer logs a *warning* and keeps the declaration
  with **zero values**, and `ComputedStyle.ApplyGlobalKeyword` then indexes past the end of
  that empty list on the first repaint — `ArgumentOutOfRangeException` the moment the
  window opens. A clean compile, a stylesheet that loads and every selector present all
  prove nothing. `HasteStylesheetTests` asserts `StyleSheet.importedWithWarnings` is false
  for exactly this reason; it was written after copying the design's timing function
  verbatim cost an afternoon.
- **Unity rewrote its own palette from IMGUI to UI Toolkit inside the Unity 6 cycle.**
  2022.3 has `QuickSearch` with `OnGUI`; 6000.x has `SearchWindow` with `CreateGUI` and a
  plain public `ListView`. IMGUI is not deprecated — 79 of 151 built-in editor windows
  still use `OnGUI` — but this specific widget was migrated.

---

Part 4 — Behaviours that look like bugs
===

These are real, currently-shipping behaviours, pinned by tests so a rewrite cannot change
them silently. Two of them arguably *should* change; that is a product decision.

4.1 ~~The first query character must begin a word~~ — FIXED
---

**This was the single biggest recall limitation and it is now fixed.** The section is kept
because the shape of the fix is the useful part.

The index bucketed only by boundary characters, and `HasteSearch.Filter` looks up exactly
one bucket keyed by the query's first character — so an item was never even scored unless
the query's **first** character started a word somewhere in it. `ollider` and `ysics`
returned **nothing**, despite `Collider` and `Physics` being indexed.

`HasteIndex` now buckets by the **distinct characters of the path**. That turns the bucket
from a wrong *subset* into a correct *superset*: a subsequence match requires every query
character to appear in the path, so it certainly requires the first one to. The
acceleration is kept; only its key changed.

"First character begins a word" survives in `HasteScoring` as a weight rather than a
filter — `INTERIOR_START_DAMPING` (0.5) halves the acronym component, and only that
component, for items the query does not start a word in. The ladder bonus is never damped,
because a literal substring is a deliberate, high-confidence signal: without that split, a
weak boundary-first match elsewhere outranks the thing the user actually typed.

Two supporting changes were needed to make the wider index useful rather than noisy:

- Two **substring rungs** were added to the ladder (`+25` name, `+15` path, both for
  queries of 3+ characters). Without them the newly reachable matches had no signal to
  rank on — the entire base score is boundary-derived, so `ollider` would have returned
  the right items all tied on zero, ordered by path length.
- `HasteSearch.Map` **drops results scoring exactly 0**. A zero score means the item
  matched only as characters scattered through word interiors, with no boundary character
  in common, no substring, and no first-character match. Those are pure noise and would
  otherwise pad the tail of every short query.

The property that makes the re-baseline reviewable: **every result that ranked before the
fix kept its exact score and its exact position.** An item could only rank at all if the
query's first character began one of its words, and those are precisely the items the
damping leaves untouched. The diff is additive.

Measured cost (macOS, 6000.3.17f1, synthetic Unity-shaped corpus, whole-search wall time —
the scheduler spreads this across frames at 16 ms each, so it is not a stall):

| Corpus | Index refs | Typical query | Worst query (`mc`) | Previously-empty (`ollider`) |
|---|---|---|---|---|
| 5,000 | 49k → 92k (1.86x) | ~1.0x, 1.4–5.4 ms | 0.95 → 2.34 ms (2.5x) | 0 → 100 results in 1.8 ms |
| 50,000 | 493k → 918k (1.86x) | ~1.0–1.1x, 11–77 ms | 16.5 → 40.8 ms (2.5x) | 0 → 100 results in 17 ms |

The 1.86x index memory is the real price. Search cost is unchanged for most queries
because the boundary bucket for a common first character was already most of the index.
(The 77 ms for `pc` at 50,000 items is **pre-existing** — it was 69.5 ms before this
change — and is worth attacking in the search-core rewrite.)

4.2 ~~Menu paths ending in `...` get a corrupted name~~ — FIXED, and it was overstated
---

`GetFileNameWithoutExtension("Component/Add...")` returned `Add..` and `GetExtension`
returned `..`. Both are fixed: a dot with another dot beside it is no longer read as an
extension separator, so the ellipsis stays part of the name. A *lone* trailing dot still
separates — `test.` still yields the name `test` — which is why the rule is adjacency
rather than "strip trailing dots".

**The claimed impact was wrong, and the correction is worth more than the fix.** This
section previously said the bug gave menu rows "a wrong display name and a bogus extension
polluting extension search". Neither was true:

- **The display name was always correct.** `AbstractHasteResult.Draw` renders
  `HasteStringUtils.GetFileName(Item.path)`, not `Item.name`, and `GetFileName` handles
  the ellipsis correctly. Nothing on screen was ever wrong.
- **There is no extension search to pollute.** `HasteItem.extensionLower` was assigned in
  the constructor and **never read by anything** — dead state costing a `GetExtension` plus
  a `ToLowerInvariant` allocation per indexed item. It has since been deleted. The
  documented `.cs` idiom works through ordinary subsequence matching on the path, because
  `GetBoundaries` emits every `.` as a boundary character. Do not "restore" extension
  search; it never existed.

What the bug actually cost was **scoring**: the exact-name, prefix-name and substring-name
rungs all compare against `nameLower`, so a menu item could not be matched by typing its
own name. Real, but narrow.

The lesson for the rest of this document: it was written partly from reading, and a claim
about impact is not the same as a claim about behaviour. Check which one you are relying on.

4.3 ~~Other known-but-unfixed items~~ — all FIXED
---

Every item once listed here is now fixed, and each is pinned so it cannot come back. They
are kept, struck through, because what each one *was* explains the shape of the fix.

- ~~`HasteIndex.Remove` decrements `Count` unconditionally, even for an item that was never
  present.~~ **Fixed** alongside 4.1: the index keeps an authoritative item set, so `Count`
  is exact, removing something never added is a no-op, and adding twice counts once.
- ~~`Haste.Update`'s frame budget captures `start` once outside the loop while accumulating
  elapsed time each iteration, producing a triangular sum — so the 16 ms budget is
  exhausted early.~~ The series was `t + 2t + 3t + …` instead of `n·t`, so the loop stopped
  after about `sqrt(2·MAX_ITER_TIME/t)` iterations rather than `MAX_ITER_TIME/t` — at a
  0.1 ms tick, ~18 per frame where the budget allows ~160. **Note what fixing it means:**
  Haste now really does spend up to 16 ms per update when there is work, which is close to
  an order of magnitude more than before. That is what the constant always claimed, but it
  has not been felt in a live editor. If it is too aggressive, lower `MAX_ITER_TIME` —
  do not reintroduce the bug.
- ~~`EditorApplication.LockReloadAssemblies` is called in `Open()` but unlocked only in a
  `new`-shadowed `Close()`, so any close path that misses the shadow leaks a reload lock.~~
  Worse than described: `Open()` locked *unconditionally*, including when a window was
  already open, so re-opening locked twice against one unlock. The lock is now balanced by
  a `holdsReloadLock` flag, taken in `InitializeInstance` and released in `OnDestroy`,
  which Unity calls however the window goes away. The shadowed `Close()` is gone, and a
  test asserts it stays gone.
- ~~`HasteStyles.WaitUntilReady()` busy-spins on `EditorStyles` forever in batch mode.~~ It
  now gives up after 600 attempts and leaves `HasteStyles.IsReady` false; `Init` returns
  early rather than throwing on the first `EditorStyles` read. The test that pins this
  would previously have *hung the whole run* rather than failed it.

One more, found while widening the index rather than inherited from this list:
`HasteScoring`'s first-character rungs indexed into `nameLower[0]` unguarded, and
`GetFileNameWithoutExtension` returns `""` for a path that is nothing but an extension — so
a GameObject named `.x` threw `IndexOutOfRangeException` out of the scorer. A good
illustration of 6.4's first rule: it compiled cleanly, and no test reached it until one was
written for it.

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
- **The recall fix (4.1)** — interior matches are reachable, acronym ranking is preserved
  by damping rather than filtering, and the golden tables were re-baselined as a
  deliberate diff.
- **The trailing-ellipsis fix (4.2)**, and a correction to what that bug actually cost.
- **The IMGUI styling layer, deleted.** `HasteStyles` and `HastePalette` went with the
  palette they existed for: ~25 GUIStyle definitions and a light/dark colour matrix, built
  at startup behind an `EditorStyles` readiness gate. Once results stopped drawing
  themselves the only caller left was the preferences page asking for one wrapped label,
  which is now a lazily-built `GUIStyle` on the page itself. The bundled `FiraSans-Regular`
  went too — it existed only for the old query field, and the new palette styles text from
  USS. **The package now ships no font at all**, which is what 5.2.2's typography note is
  about.
- **The obsolete-API burn-down, complete.** 32 warnings to 0. Beyond the prefab work below:
  scene and selection tracking moved off per-frame polling of `EditorApplication.currentScene`
  and `Selection.activeInstanceID` onto `EditorSceneManager`'s events and
  `Selection.selectionChanged` (3.3's warning applies — `activeEntityId` does **not** exist
  in 6000.0, so `Selection.objects` is the portable answer); `projectWindowChanged` and
  `hierarchyWindowChanged` became `projectChanged`/`hierarchyChanged`; `EditorWindow.title`
  became `titleContent`; and `[PreferenceItem]` became `[SettingsProvider]`, which also
  makes the preferences page searchable.
- **The prefab-API migration** off the obsolete `PrefabType`, which cleared 14 of the 25
  obsolete warnings. Measured mapping, because the obvious replacement is the wrong one:
  `GetPrefabAssetType` answers `Regular` for a prefab *instance* as well as for the asset,
  so "is this an asset?" is `IsPartOfPrefabAsset`, and "how should this row be coloured?"
  is `GetPrefabInstanceStatus`. Prefab variants, which postdate the original code, are now
  handled like any other prefab. `GameObject/Reconnect to Prefab` was deleted: its API's
  own obsolete message is "This method does nothing."
- **The live-menu rewrite.** `HasteMenuItemSource` reads the editor's menu tree instead of
  shipping hardcoded tables. This was the tool's biggest correctness problem and it was
  measured, not guessed: of the 241 Unity 5 paths being indexed on 6000.3.17f1, **109
  (45%) did not exist** — results that looked real and did nothing — while **384 real menu
  items were missing**. Deleted with it: `MenuItemsUnity4.cs` and `MenuItemsUnity5.cs` (479
  literals), `HasteVersionUtils.cs` (now unused), and 33 of the 44 `HasteActions`
  fallbacks. 76 tests, all passing.

5.2 Not done
---

**The obsolete-API burn-down is finished: 0 warnings, down from 32, and the UI Toolkit
palette has landed.** The remaining phases, in order: event-driven incremental indexing
with stable identity keys; the rest of the search-core rewrite; and settings consolidation.

Left over from the palette work, in rough order of value:

- **"Suggested commands"** on the launch screen. Recents are real; suggestions need a
  signal that does not exist yet.
- **Row density** is fixed at the design's standard 32px. The design exposes compact (24)
  and two-line (40) as well; they would be a preference.
- **The Layout source duplicates the Menu Item source.** Measured on 6000.3.17f1: the live
  menu already contains `Window/Layouts/Default`, `/Tall`, `/Wide`, `/2 by 3`, `/4 Split`,
  so `HasteLayoutSource` re-indexes paths `HasteMenuItemSource` has already yielded. Dedupe
  is per-source, so a saved layout can appear twice, once weighted as `Layout` and once as
  the `Window` menu root. The layout paths themselves are still correct — that was checked
  when the `Window/Project` bug was fixed, and it is not another instance of it.

3.2's `HierarchyProperty` note is worth reading before the incremental-indexing work: it is
public in Unity 6 and gives name, depth, instanceID and `colorCode` with no managed object
loads, which is what `HasteHierarchySource` and `ClassifyPrefab` currently do the slow way
— re-crawling `Resources.FindObjectsOfTypeAll<GameObject>()` and asking `PrefabUtility` per
object. Pair it with `ObjectChangeEvents.changesPublished` and the traps in 3.3 about
`CreateGameObjectHierarchy` coalescing and
`ChangeGameObjectOrComponentProperties` being the only rename signal.

See `Documentation~/activation-design.md` for the activation design. Both tiers are now
implemented: the `[Shortcut]` binding and the double-tap-Shift gesture.

5.2.1 What the menu rewrite deliberately did not do
---

- **`ExecuteMenuItem` equivalence for the 10 live fallbacks is taken on trust.** Ten of the
  33 deleted fallbacks (`Edit/Undo`, `Edit/Copy`, `File/New Scene`, …) were keyed on paths
  that *do* still exist, so deleting them changes what runs. The grounds are 3.2's finding
  that `ExecuteMenuItem` is backed by the same native menu tree the source now enumerates,
  plus the observation that each deleted body was equivalent or worse — `File/New Scene`
  called the obsolete `EditorApplication.NewScene()` rather than opening Unity 6's
  scene-template dialog, and the clipboard entries posted a command event to
  `EditorWindow.focusedWindow`, which can be null. **None of this was observed running.**
  Press Undo, Copy and New Scene from the palette in a real editor before trusting it.
- **The macOS application menu is gone from the index**, deliberately. `About Unity` and
  `Settings…` live there, `Menu.GetMenuItems("Unity")` returns 0, and the old hardcoded
  entries came with fallbacks that reflect into `UnityEditor.PreferencesWindow` — a type
  whose continued existence nobody has checked. Re-adding them means a modern
  implementation (`SettingsService.OpenUserPreferences`) and a platform-correct path, on a
  machine that can test both platforms.
- **Root discovery costs ~120 ms** and cannot be filtered down by assembly name: the cost
  is spread across a hundred assemblies, and `Services` is declared by
  `UnityEditor.Purchasing` and `UnityEditor.UnityConnectModule`, so excluding Unity's own
  assemblies loses a real menu. It is cached per domain and deferred until after the
  built-in roots have been yielded, so the ~500 common menu items are searchable in about
  a millisecond and the scan is paid once, in the background, after them. If it ever needs
  to be cheaper, the honest fix is a cache keyed on assembly MVIDs, not a name filter.

5.2.2 Unverified — needs the editor open
---

From the palette rewrite, in the order worth checking:

- **Whether anything useful went missing from the actions pane.** `HasteMenuTree` drops
  `Assets` entries that stay enabled with an empty selection, on the grounds that they
  cannot be acting on the selection. Measured on 6000.3.17f1: 17 non-`Create` entries hit,
  15 of them correctly (including all 7 `Mobile Dependency Resolver` entries, caught
  without the rule knowing that package's name), 2 false positives now on the keep-list.
  **The rule is deliberately scoped to `Assets` alone** — applying it to `GameObject` cuts
  24 entries to 3, because Unity's GameObject items mostly declare no validate function
  and so read as always-enabled. Measure before adding a root to `RuleRoots`.
- **What availability filtering feels like day to day.** `HasteSearch.Map` drops any menu
  item `Menu.GetEnabled` reports disabled, so results change with the selection. Measured
  on a stock 6000.3.17f1 with nothing selected: 241 of 538 menu items hidden, including
  all 172 under `Component/`. It runs other people's validate functions on every keystroke
  — 0.35 ms for a warm pass over all 538, 7.9 ms on the first call after a domain reload.
  If a project's validators ever make that hurt, `HasteMenuItemSource.IsAvailable` is the
  line to put behind a preference.
- **The nested actions pane.** Right arrow opens the item's real context menu
  (`HasteMenuTree`), and right arrow again descends into a submenu. The tree, the
  ordering, the submenu detection and the enabled-filtering are all covered by
  `HasteMenuTreeTests` against the live editor, but *walking* it is window code: that
  `EnterSubmenu` redraws the pane, that the breadcrumb reads correctly, and that a menu
  item run from three levels down acts on the right object. Note the pane SELECTS the row
  when it opens — `Menu.GetEnabled` runs validate functions against the selection, and
  without it every row-specific entry filters out. Measured on 6000.3.17f1: 17 of 33
  top-level `Assets` entries enabled with nothing selected, 28 with an asset selected.
- **That focus never leaves the query field.** `OnQueryFocusOut` re-focuses on the next
  panel tick, because UI Toolkit's focus controller blurs on pointer-down and finishes
  after the click callback — a `Focus()` made during the click is undone. That is runtime
  UI Toolkit behaviour with no headless surface, so the one-tick restore has not been seen
  working, and whether it flickers is unknown. If it does, the alternative is preventing
  the blur at `PointerDownEvent` rather than undoing it, which risks suppressing the
  `MouseDownEvent` the rows and chips are built on.
- **That the new row icons read at 16px.** `HasteIcons` maps every kind to a built-in
  editor icon and a test proves each name resolves, but not that it *looks* right —
  `_Menu` in particular is a small monochrome hamburger glyph sitting next to full-colour
  asset icons.
- **That it opens at all, focused, with the caret in the field.** `Update` closes the
  window when it is not the focused window, and only a `hasBeenFocused` guard stops that
  firing on the frames between `ShowPopup` and focus arriving.
- ~~**The corners.**~~ SETTLED, and the answer was worse than "renders square": the radius
  DID apply, carving the palette's corners away and showing the backdrop plate behind them,
  so the window read as a rounded card on a dark rectangle. The window is square now.
  Rounding it for real needs per-pixel window transparency, which UI Toolkit does not
  expose; the radius stays on rows, chips and badges, which have a surface behind them.
  The design's drop shadow is still simply absent — USS has no box-shadow.
- **The text field.** Unity's `TextField` has its own nested input element and default
  chrome; the stylesheet targets `.unity-text-field__input` to strip it. If a border or a
  pale background survives, that selector is the place to look.
- **Highlighting on a short name.** `GetWeightedSubsequence` prefers word boundaries, so
  "popup" in `Popup.prefab` bolds P-o-p-u and then the `p` of `.prefab`. Correct for
  acronyms, odd on a short name shown by itself — which the new row does far more than the
  old full-path label did.
- **Arrow keys.** They are intercepted on the root with `TrickleDown` so the text field
  does not eat them first. Which key does what is pinned by `HasteKeyMapTests`; that the
  events arrive at all is not, and cannot be.
- **The actions pane slide.** `→` moves a double-width track by `translate: -50%` over
  0.18s, eased with `ease-in-out` because USS has no `cubic-bezier` (see 3.3).
- **Destructive actions.** Delete opens a confirmation dialog, and it must do so *after*
  the palette has closed — the window dismisses on focus loss, so a modal raised while it
  is open would pull the palette out from under the dialog. Worth confirming once.
- **Typography.** The design sets paths, type badges and key chips in a monospace
  (`ui-monospace, Menlo`) and everything else in the system UI face. The stylesheet sets
  neither, so all of it renders in Unity's default editor font — the proportional text is
  close, the monospace runs are not. Fixing it means shipping a font and pointing
  `-unity-font-definition` at it; the package deliberately ships none (see 5.1).

From the burn-down:

- **The preferences page has not been looked at since it moved to `[SettingsProvider]`.**
  Its body is unchanged and still draws its own `HasteScrollView`, which now sits inside
  the Settings window's own scrolling content area. Everything stays reachable either way,
  but a nested scrollbar is the kind of thing only a human sees. Open
  Preferences > Haste.
- **Scene-change handling changed shape, not just API.** Additively opening or closing a
  scene now rebuilds the hierarchy index; polling the "current scene" could not notice
  that. Worth exercising with a multi-scene setup.

5.3 Open product decisions
---

1. ~~Whether the recall fix (4.1) drops boundary-first entirely or keeps it as a boost.~~
   **Decided: kept as a boost**, implemented as `INTERIOR_START_DAMPING` applied to the
   acronym term only. Two follow-on calls were made in the same change and are the ones
   worth revisiting: the damping constant itself (0.5, unmeasured against real usage), and
   dropping zero-scored results outright. A weak tail survives — `amera` still returns
   `GameObject/Create Empty` at 9 on one shared boundary character. Tightening that needs
   a real corpus and real users, not more derivation.
2. Whether to index only mutable packages, all packages, or none.
3. ~~Whether the movable-window mode survives, given the centring bug that motivated it is
   being fixed.~~ **Decided: removed.** The centring bug is fixed —
   `Screen.currentResolution` reports the primary display regardless of which monitor Unity
   is on, and `HasteDisplay` now centres on the editor's own window — so the workaround has
   nothing left to work around. The new palette is a fixed borderless popup with no drag
   affordance, so the toggle would have been a preference that does nothing.
4. ~~Ignore-list scope (team-shared vs per-user) and semantics (literal prefixes vs
   globs).~~ **Decided: both scopes, and a small syntax instead of globs.** A shared list
   in `ProjectSettings/HasteIgnorePaths.asset`, the user's own in EditorPrefs, and a
   shipped default list — all three unioned. A rule containing `/` is a path prefix; one
   without is a folder name matched at any depth; a leading `!` is an exception that always
   wins, which is what keeps `Plugins/Android` searchable while `Plugins` is ignored.
   Matching is `OrdinalIgnoreCase` and boundary-aware, replacing a culture-sensitive
   `IndexOf(rule) == 0` that also matched `Assets/PluginsCustom` for a rule of
   `Assets/Plugins`.

   **The shipped rules are all path-rooted, and that was measured rather than assumed.**
   The first attempt matched SDKs by bare name so they would be caught wherever they were
   unpacked. Run against a real 20,000-file project it hid
   `Assets/Scripts/Firebase/FirebaseService.cs` — the project's *own* wrapper code, named
   after the SDK it wraps, which is entirely normal. A shipped list cannot see the
   project's layout and has no business guessing at depth; a user's own rules can, so bare
   names remain available there. `HasteIgnoreTests` pins that case.
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

On macOS the editor binary is inside the app bundle; the arguments are identical. A full
run from a cold `Library/` takes about two minutes:

```bash
/Applications/Unity/Hub/Editor/6000.3.17f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath <proj> -runTests -testPlatform EditMode -testResults <xml> -logFile <log>
```

Gate on the results XML showing `failed="0"` **and** `total > 0`, not on the exit code
alone: a run-level failure can still exit 0. Exit 2 means test failures.

Work against a **copy** of the project, not the repo, so a failed run cannot leave the
working tree upgraded or locked. Each editor version needs its own copy, because `Library/`
is version-specific. This matters more than it sounds: `ProjectSettings/ProjectVersion.txt`
still reads `5.1.1f1`, so any run pointed at the repo silently upgrades it and dirties the
tree. `rsync -a --exclude .git --exclude Library --exclude 'Image Assets'` gives a ~25 MB
copy that runs fine.

6.1.1 The palette cannot be verified this way at all
---

`HasteSpotlightWindow` is UI Toolkit, and none of it — layout, styling, focus, keyboard —
runs under `-batchmode`. What is pinned instead is everything that could be moved out of
the window: the kind taxonomy and scope-token parsing (`HasteKindTests`), multi-term
matching and per-part highlighting (`HasteCharacterizationTests`), and the stylesheet's
packaging plus the fact that it declares every selector the window adds
(`HastePackageTests`). A renamed selector is otherwise a silently unstyled window rather
than an error.

That leaves a real list needing a human at the keyboard, and it is written down in 5.2.2
rather than assumed away.

6.2 What headless verification cannot tell you
---

This has already cost real bugs. A green compile and a passing `-nographics` suite do not
exercise:

- editor GUI, styles, fonts or GUISkin (`EditorStyles` throws in batch mode regardless of
  graphics device)
- anything downstream of an `EditorStyles` readiness gate
- real keyboard or mouse input, on any platform. The palette's answer is
  `HasteKeyMap.Resolve`, a pure function from (key, modifiers, mode) to an intent, which
  `HasteSpotlightWindow.OnKeyDown` does nothing but dispatch. That exists because the right
  arrow once shipped doing nothing at all: an edit adding the binding was lost, the code
  compiled, every test passed, and the only way to find it was to press the key. A binding
  that lives in a `switch` inside an `EditorWindow` is untestable; the same binding in a
  pure function is ordinary
- `[InitializeOnLoad]` → scheduler startup chains, since `-quit` exits before
  `EditorApplication.update` pumps them

For those, open the editor. Pin the observable *precondition* in a test instead of the
crash, add a reflection guard so a deleted landmine stays deleted, and then say plainly
that confirmation needs a human.

6.3 macOS
---

**This section's premise has changed.** It was written on a Windows machine with no Mac
available, which is why so much of the activation design is deferred rather than decided.
Development now happens on macOS with **6000.3.17f1** installed, and the suite has been run
there: 70 tests green, 0 compile errors, 32 obsolete-API warnings.

What that does *not* change: **6000.0.80f1 is not installed on the Mac**, so the
dual-editor verification the rest of this document assumes is currently single-editor.
`package.json` still declares `"unity": "6000.0"`. Treat 6000.0 as unverified until someone
installs it or the floor is raised deliberately.

What it does change: the two things marked "genuinely unknown on macOS" below, and the
matching pair in `activation-design.md`, are now answerable by experiment. Stop deferring
them.

The rules themselves all still stand, because they are about writing portable code, not
about which machine compiles it:

- Branch on `Application.platform` at **runtime**, never `#if UNITY_EDITOR_OSX` — a
  Windows-built editor assembly bakes in the compiling editor's symbol.
- Use `ShortcutModifiers.Action`, which resolves to Cmd on macOS and Ctrl elsewhere at
  runtime. Never `ShortcutModifiers.Control`, which means the literal Ctrl key even on a Mac.
- Use `AssetDatabase`'s already-`/`-separated project-relative paths rather than string
  surgery on `Application.dataPath`.
- Make every path and prefix comparison **`Ordinal`**. The development machine runs
  `tr-TR`, where `"I".ToLower()` is dotless `ı` and culture-sensitive prefix matching
  genuinely diverges from ordinal.
- Two things were listed as genuinely unknown on macOS: whether a borderless popup takes
  keyboard focus without an activating click, and whether a bare Shift press is delivered
  as a KeyDown at all (macOS reports modifier changes as `flagsChanged`). Both are now
  **testable rather than unknown** — but neither has been tested yet, and neither can be
  settled headlessly (see 6.2). Open the editor.

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
