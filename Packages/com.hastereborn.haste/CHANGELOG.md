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
- Menu-item search no longer dies at startup. The shortcut-stripping regular expression
  contained `\_`, which is not a legal escape sequence; modern .NET throws while
  constructing it, so the whole menu source failed with a `TypeInitializationException` on
  every scheduler tick. Unity 5's older Mono tolerated it.

### Removed
- The Asset Store upsell and the free/Pro distinction.
- The update checker, which polled a domain that no longer exists over the obsolete `WWW`
  class. It was the only network call in the tool.
- The bundled `UnityTestTools` copy from 2015, superseded by `com.unity.test-framework`.
- The `System.CodeDom` DLL export pipeline, which targeted hardcoded macOS Unity 5 paths.
