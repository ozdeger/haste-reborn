Changelog
===

All notable changes to this package. This project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0-pre.1] - unreleased

The Unity 6 revival. Haste last shipped as 1.8.6 for Unity 5.1 in 2019; the historical
changelog for those releases is in the repository root.

### Added
- Assembly definitions (`Haste.Editor`, `Haste.Editor.Tests`), both Editor-only. Haste no
  longer compiles into `Assembly-CSharp-Editor`, so unrelated broken scripts in a project
  can no longer stop it from building — and vice versa.
- Distribution as a UPM package installable from a git URL.
- A characterization test suite pinning the search ranking, the scoring ladder and the
  fuzzy-match highlighting, so behaviour changes are visible rather than silent.

### Changed
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

### Fixed
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
