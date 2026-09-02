Changelog
===

All notable changes to this package. This project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0-pre.1] - unreleased
The Unity 6 revival. Haste last shipped as 1.8.6 for Unity 5.1 in 2019; the historical
changelog for those releases is in the repository root.

### Added
- **Tap Shift twice to open Haste.** A second way in, alongside the keyboard shortcut. It
  stays out of the way: ignored while you are typing in a field, while dragging, in play
  mode, and while Haste is indexing, and it will not mistake holding Shift for capitals as
  two taps. If it ever misfires repeatedly it switches itself off rather than keep
  interrupting. Tune the tap window, or turn it off, under Preferences ▸ Haste.
- Assembly definitions (`Haste.Editor`, `Haste.Editor.Tests`), both Editor-only. Haste no
  longer compiles into `Assembly-CSharp-Editor`, so unrelated broken scripts in a project
  can no longer stop it from building — and vice versa.
- Distribution as a UPM package installable from a git URL.
- A characterization test suite pinning the search ranking, the scoring ladder and the
  fuzzy-match highlighting, so behaviour changes are visible rather than silent.

- **Favorites.** Press `Alt+Enter` on a row to favorite or unfavorite it, or
  right-click an asset and choose `Haste > Add to Favorites`. A favorite scores
  2× on top of every other weight, and its row shows the editor's star beside the
  name. The list is under Preferences > Haste, where entries can be removed.
  Favorites live in this project's `UserSettings` folder — yours, not committed.

  Scene objects cannot be favorited: a favorite is remembered by path, and a
  GameObject's path changes when it is renamed, reparented or its scene closes.

- Menu items are now weighted by their menu rather than all together. Unity's own menus
  start demoted at 0.7 as before, but a menu your project added — `Tools`,
  `Dev Tools`, anything a `[MenuItem]` invented — starts at 1.0 and gets its own
  slider under Preferences > Haste > Weights by menu as soon as Haste sees it.

### Changed
- **Vendored and generated folders are ignored out of the box.** `Assets/Plugins`, the
  External Dependency Manager's folders, the common mobile SDKs and Unity's own magic
  folders no longer appear in results, so a search for "manager" returns your managers
  rather than a third-party library's. Measured on a real 20,000-file project, that is
  about 10% of all assets and a quarter of every C# hit.
  - `Plugins/Android`, `Plugins/iOS` and `Plugins/tvOS` stay searchable, because
    `AndroidManifest.xml` and native plugin sources are things people genuinely look for.
  - See exactly what is in the list, or turn it off, under Preferences ▸ Haste.
- **Ignore paths can be shared with your team.** Two lists now: one committed to the
  project in `ProjectSettings/HasteIgnorePaths.asset`, and your own, which stays on your
  machine. Both apply.
- **Ignore rules got a syntax.** A rule with a slash is a path (`Assets/Plugins`). A rule
  without one is a folder name matched at any depth (`Firebase`). Start a rule with `!` to
  make an exception, which always wins.
- **"Enable Select" is now off by default.** Arrowing through results no longer selects
  each one as you pass it. That preview was useful, but it expanded hierarchy and project
  folders as it went — and unlike the selection, that rearrangement was not undone when you
  pressed Escape, so just browsing left the editor changed. Turn it back on in
  Preferences ▸ Haste; if you had already set it either way, your choice is kept.
- **Enter reveals, Shift+Enter opens.** Enter still focuses and selects the thing, as it
  always did. Shift+Enter now opens it in whatever edits it — a script in your IDE, a scene
  in the editor, a prefab in Prefab Mode. On a GameObject that came from a prefab, it opens
  that prefab. Where opening means nothing, Shift+Enter simply does what Enter does.
- **Item actions.** Press `→` (or `Cmd/Ctrl+K`) on a result to slide across to what you can
  do with it — reveal it, show it in Finder, copy its path or GUID, duplicate it, delete it.
  `←` or Escape goes back. Which actions appear depends on what the thing is; a menu
  command has no GUID to copy.
- **Rows use the editor's own icons** — the prefab, texture, audio clip, animation clip and
  controller icons you already recognise, straight from the Project window. Menu commands
  and window layouts, which have no asset to take an icon from, keep a text badge.
- **A new palette.** Haste is rebuilt on UI Toolkit against a Spotlight-style design:
  a wider window, a single row per result with the file name on the left and its folder on
  the right, a type badge, and a status bar. Matched characters are still highlighted, now
  in both the name and the folder.
- **Results can be weighted by type.** Every result's score is multiplied by a per-type
  weight, so whole categories can be pushed down without being hidden. Scene objects start
  at 0.5, and menu commands, components, tools and layouts at 0.7 — there are hundreds of
  each and they match short queries readily, which was burying project assets. Everything
  in the project stays at 1. Adjust them under Preferences ▸ Haste ▸ Result Weights; they
  are yours rather than the project's, and stay on your machine.
- **Filter by type with `t:`** — the same syntax Unity's own Project search uses:
  `t:prefab`, `t:texture`, `t:audio`, `t:anim`, `t:animator`, `t:material`, `t:model`,
  `t:shader`, `t:font`, `t:script`, `t:scene`. `h:` scopes to the open scenes, `>` to menu
  commands, `#` to components. The prefix turns into a chip; backspace on an empty query
  clears it.
- **Type filters apply to the recent list too.** Picking a type with an empty query used
  to leave every unrelated recent on screen, so the filter looked like it had done nothing.
- **The chips under the search field are buttons now.** Clicking one types its tag, in
  front of whatever you have already typed — so type "popup", click `t:prefab`, and you
  have narrowed rather than started over.
- **Spaces in a query now separate terms instead of killing the search.** Every term has
  to match, in any order, so "main camera" finds `MainCamera.mat` and "popup crimescene"
  finds `Popup_CrimeScene_Character_Banner_Sale.png`. Previously the whole query was one
  subsequence, so the space had to occur literally in the path — and paths rarely contain
  one, which meant typing a space usually emptied the result list outright.
- Haste's preferences page is now searchable. It moved from the deprecated
  `[PreferenceItem]` to Unity's settings system, so it stays in the same place in
  Preferences but also turns up when you type "haste", "fuzzy" or "index" into the
  settings search box.
- Haste no longer polls the editor. It used to compare the current scene and the current
  selection against cached copies on every editor update, several times a second, forever;
  it now subscribes to the corresponding editor events. Opening a scene additively also
  rebuilds the hierarchy index now, which polling the "current scene" could never notice.
- Prefab handling moved onto Unity's current prefab API. Prefab **variants** are now
  handled like any other prefab — they are kept out of the hierarchy results the way plain
  prefabs always were, and `Instantiate Prefab` works on them. Variants did not exist when
  the old code was written, so it did neither.
- **Menu search now reads the editor's live menus** instead of a list of menu paths
  captured from Unity 5. On Unity 6 that list was 45% wrong: 109 of its 241 paths no
  longer existed, so Haste offered menu items that looked real and did nothing when you
  pressed Enter, while 384 menu items that do exist were missing entirely. Haste now finds
  built-in menus, package menus and your own `[MenuItem]` methods, including ones under a
  menu root of your own invention.
- **Search now finds interior matches.** A result used to be considered only if the first
  character you typed began a word in it, so `ollider` found nothing at all even though
  `Mesh Collider` was indexed, and `amera` could not find `Main Camera`. Both work now, as
  does every other query that starts mid-word.
  - Acronyms still win. `mc` still puts `Main Camera` first, and every result that ranked
    before this change kept its exact score and position; interior matches are damped and
    appear beneath them.
  - Typing a literal substring of three or more characters now scores for itself directly.
    Without that the newly-reachable results had nothing to rank on and came back in
    effectively arbitrary order.
  - A result that matches only as scattered characters, with no other signal at all, is no
    longer shown rather than padding the end of the list.
  - The index holds about 1.9x as many references as a result. Search speed is unchanged
    for most queries; the worst case measured on a synthetic 50,000-item project was 2.5x,
    still inside the editor's per-frame budget.
- **The default shortcut is now `Ctrl/Cmd+Shift+K`**, and it is registered with Unity's
  shortcut system so it can be rebound in Edit > Shortcuts instead of by editing source.
  Haste previously shipped `[MenuItem("Window/Haste %k")]`, but Unity 6 binds the same
  `Ctrl/Cmd+K` to its own Search window — and when two commands share a chord, one of them
  silently never opens. `ShortcutModifiers.Action` resolves to Cmd on macOS and Ctrl
  elsewhere at runtime, so one declaration covers both platforms.
- Every feature is unconditionally enabled. The `IS_HASTE_PRO` compile define used to gate
  menu-item search, recency-based recommendations and menu-item actions behind a "Pro"
  edition, and nothing in the repository ever defined it — so building from source
  silently produced the crippled free edition.
- Packaged resources are located by package-relative path instead of scanning every asset
  path in the project looking for the plugin's own folder.
- The recency store moved from a `ScriptableObject` written into the plugin's own folder to
  `UserSettings/HasteRecency.asset`. The old location breaks as soon as the package is
  installed read-only. Pre-2.0 recency data is discarded rather than migrated: it
  identified items by an unstable hash that cannot be resolved back to an object.

- `Shift+Left` and `Shift+Right` move the caret through the query, since the
  plain arrows drive the palette. They move it rather than selecting with it.
- Moving between levels of a context menu animates instead of snapping: a short
  14px throw in the direction you are going, plus a fade. The full-width slide it
  replaced dragged a pane of wide rows across the screen, which reads as
  direction but is unpleasant to watch.
- The slide into the actions pane is faster — 0.12s and ease-out, where an
  ease-in-out curve's slow start was most of what read as a wait.
- The palette header shows Haste's own mark instead of the placeholder letter
  the design comp used: a 48px square in the window's top-left corner, with no
  chip around it.
- Escape always closes Haste, wherever you are. It used to unwind one level of
  the actions pane, so the deeper you were the more times you had to press it.
  Going back is the left arrow's job and only the left arrow's.
- The badge at the left of the footer is now a settings button that opens Haste's
  preferences. It still doubles as the indexing light.
- The footer shows a `Favorite` / `Unfavorite` hint alongside the others, and
  clicking it works. It hides itself for a row that cannot be favorited.
- The actions pane scrolls, and its rows are built from the same measurements as
  the results list beside it — same height, padding, corner radius, hover and
  selection greys. A long context menu used to squash its rows to a third of
  their height with no way to reach the ones past the bottom.
- Type filters are now recognised **anywhere in the query**, not only at the
  front. Search for `popup`, then type ` t:prefab ` on the end and it becomes a
  chip like it always should have. `prefab:` works the same way. Sigils (`>`,
  `#`, `?`) still only bind at the very start, where they are unambiguous.
- The palette window is square. The design's 12px radius cannot round a Unity
  popup — an editor window has no per-pixel transparency, so the radius carved
  the corners out and left a rounded card sitting on a visible darker plate.
  Rows, chips and badges keep their radius, where there is a real surface to
  round against.
- The actions pane no longer lists the `Assets` menu's project-wide entries —
  `Refresh`, `Reimport All`, `Import New Asset…`, `Import Package`,
  `Open C# Project`, `Update UXML Schema`, `View in Import Activity Window`, and
  any package's own project-wide tooling. They act on the project, not on the
  asset you opened the pane for. `Create`, `Reveal in Finder` and
  `Select Dependencies` are kept explicitly.
- Menu items you cannot use right now no longer appear in results. A menu the
  editor would draw greyed out does nothing when you press Enter, so it is not
  offered — with nothing selected that hides 241 of the 538 menu items, including
  every `Component/…` entry, and they come straight back the moment you select
  something.
- The actions pane (right arrow) now shows the item's **real context menu** — the
  editor's own `Assets` menu for a project asset, `GameObject` for a scene object,
  including whatever your packages have added to them, and filtered to what is
  actually available for that item. Submenus nest: right arrow (or Enter) goes in,
  left arrow comes back out, Escape closes the pane. Menu items and layouts keep
  the built-in actions, since nothing right-clicks them.

  This replaces the hand-written list for assets. `Copy Path`, `Delete`, `Open`
  and `Reveal in Finder` are all in Unity's menu already; **`Copy GUID` and
  `Duplicate` are not, and are gone** — say the word and they can come back as
  extra rows.
- The `>` filter chip is now `t:menu`, and `h:` moved to the front of the row.
- Menu items and window layouts now show an editor icon instead of a text badge,
  so every row reads the same way. Any row whose icon cannot be resolved falls
  back to its type icon rather than to text.
- `Tools/…` entries are no longer a separate type. They are menu items under a
  root some package added, they get the menu icon like any other, and per-menu
  weights are the knob for treating them differently. `t:tool` still works and
  scopes to menu items.
- The query field keeps keyboard focus for as long as the window is open —
  clicking a filter chip, a row, or an action no longer leaves you typing into
  nothing.
- The filter for menu items is called `menu`, not `command` — `t:menu`, or the `>`
  sigil as before, and the chip reads "menu". `t:command` and `t:cmd` still work
  but are no longer the name. The row badge reads `MENU`.

### Fixed
- The query line and its caret no longer move when you type the first character.
  Both the slot and the inner text element were sized by their content, and an
  empty text field measures differently from one with a character in it.
- The actions pane scrolls with the arrow keys at all — it was scrolling rows
  that had no size yet, which does nothing.

- Pressing Enter on a project asset no longer logs an error instead of focusing it. Haste
  was still asking for Unity 5's `Window/Project` menu item, which Unity 6 moved under
  `Window/General/`. The window menus are now looked up from the editor rather than
  written down.
- Ignore rules no longer match partial folder names. `Assets/Plugins` used to also hide
  `Assets/PluginsCustom`, because the comparison had no segment boundary — and it was
  culture-sensitive, which diverges in Turkish locales.
- The palette no longer throws `ArgumentOutOfRangeException` when it opens. Its stylesheet
  used a CSS timing function that UI Toolkit does not support; Unity keeps such a
  declaration with no values rather than rejecting it, and then reads past the end of it on
  the first repaint.
- Selecting a hierarchy result no longer throws if the object was destroyed between the
  search and pressing Enter.
- Haste no longer leaks a script-compilation lock. It holds one while the palette is open
  so the window cannot be destroyed mid-use, but released it only on one of several ways
  the window can close — and took a second lock if you reopened the palette while one was
  already open. A leaked lock is invisible and stops your script changes compiling until
  you restart the editor.
- Indexing and search now get the full 16 ms per editor update they were designed around.
  An error in the budget arithmetic meant they were getting closer to a tenth of it, so
  large projects took far longer to become searchable than intended.
- Menu items ending in "..." are no longer mis-parsed as having an extension of "..", so
  they can be found by typing their own name. A lone trailing dot still separates an
  extension, so a file named "test." is still named "test".
- Scoring no longer throws on an item whose name is empty. A path that is nothing but an
  extension — a GameObject named ".x", say — produced an empty name, and the scorer
  indexed into it unguarded.
- Removing an item that was never indexed no longer corrupts the index's item count, and
  adding the same item twice no longer counts it twice.
- Prefix and path comparisons in scoring use ordinal comparison. Culture-sensitive
  comparison genuinely diverges in Turkish locales, where lowercasing "I" does not give "i".
- Menu-item search no longer dies at startup. The shortcut-stripping regular expression
  contained `\_`, which is not a legal escape sequence; modern .NET throws while
  constructing it, so the whole menu source failed with a `TypeInitializationException` on
  every scheduler tick. Unity 5's older Mono tolerated it.

### Removed
- The IMGUI styling layer that the old palette needed — twenty-five editor styles and a
  light/dark colour matrix built every time Unity started, plus the font bundled with them.
  The new palette styles itself, and the preferences page needs exactly one label style.
- The movable-window mode, and with it the "Window Position" preferences section. It
  existed because the palette used to mis-centre itself, opening on the primary display
  regardless of which monitor Unity was on. That is fixed — the palette now centres on the
  editor's own window — so the workaround has nothing left to work around.
- The `Reconnect to Prefab` action. Unity 2018.3 rebuilt the prefab system and removed
  disconnected prefab instances, so there is nothing left to reconnect — the API behind it
  does nothing at all, and the action was a row in the palette that silently did nothing
  when you pressed Enter.
- An unread extension field on every indexed item, which cost a string scan and an
  allocation per item during indexing and misleadingly implied that searching by extension
  worked differently from any other search. It does not: `.cs` matches the same way
  everything else does.
- The two hardcoded menu-path tables (Unity 4.6 and Unity 5, 479 string literals) and the
  version check that chose between them.
- 33 of the 44 entries in the menu-item action table: hand-written stand-ins for built-in
  menu items from the days when `ExecuteMenuItem` could not reach them. 23 were keyed on
  Unity 5 paths that no longer exist and could never fire; the other 10 shadowed working
  menu items with worse behaviour. The 11 actions Haste implements itself are unchanged.
- The Asset Store upsell and the free/Pro distinction.
- The update checker, which polled a domain that no longer exists over the obsolete `WWW`
  class. It was the only network call in the tool.
- The bundled `UnityTestTools` copy from 2015, superseded by `com.unity.test-framework`.
- The `System.CodeDom` DLL export pipeline, which targeted hardcoded macOS Unity 5 paths.
