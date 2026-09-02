Haste Search Engine for Unity 3D
===

Haste is a search engine for Unity 3D. Navigate your project with speed.

> "It’s like Spotlight or Alfred for Unity", said by a friend of ours.

**Usage: Open Haste by pressing Command/Control+Shift+K — or tapping Shift twice — and
begin typing to search.**

Development
---

If you are picking up work on Haste, start with [HANDOFF.md](HANDOFF.md): what the tool
does, how it is built, verified Unity 6 API facts, known behaviours that look like bugs,
and the current state of the revival.

Installation
---

In Unity's Package Manager choose **Add package from git URL** and paste:

```
https://github.com/ozdeger/haste-reborn.git?path=/Packages/com.hastereborn.haste
```

Append a tag to pin a release, e.g. `#v2.0.0`. Requires Unity 6000.0 or newer.

The `?path=` is required: this repository is a Unity project that *contains* the package
at `Packages/com.hastereborn.haste`, so a bare repository URL will fail with
"Repository does not contain a package manifest".

Screenshots
---

![GameObjects](Images/GameObjects.png)
![Assets](Images/Assets.png)
![MenuItems](Images/MenuItems.png)
![History](Images/History.png)
![Recommendations](Images/Recommendations.png)
![Preferences](Images/Preferences.png)

Features
---

- Locate game objects in your scene, project assets and menu items.
- Use multi-select and drag and drop to work more efficiently.
- Configure the keyboard shortcut to personalize your workflow.
    - Search faster with "fuzzy" matching: just type “mc” for "Main Camera".
- Get intelligent search recommendations based on what you search for the most.
- Execute native Unity menu items or extend Haste using custom “MenuItem” attributes
- Selectively ignore assets from search results.

Reference
---

Action | Keyboard | Mouse
---|---|---
Open Haste | ⌘ + ⇧ + K (Ctrl + Shift + K on Windows), or tap ⇧ twice | Click "Window/Haste"
Navigate Search Results | ↑ or ↓ | Click search result
Reveal Highlighted Result | Enter | Double-click search result
Open Highlighted Result | ⇧ + Enter |
Show Item Actions | → (or ⌘/Ctrl + K) | Click "Item actions"
Go to beginning | Fn + ← (Home on Windows) |
Go to end | Fn + → (End on Windows) |
Go up a page | Fn + ↑ (Page Up on Windows) |
Go down a page | Fn + ↓ (Page Down on Windows) |
Multi-Select Highlighted Result | ⌘ + Enter (Ctrl + Enter on Windows) | ⌘ + Click (Ctrl + Click on Windows)
Dismiss Haste | ESC | Click anywhere outside of Haste

Configuring Haste
---

Haste's shortcut is registered with Unity's shortcut system, so you rebind it the same way
you rebind anything else in the editor: **Edit > Shortcuts**, then search for `Haste`.
There is no need to edit any source file.

The default is `Ctrl/Cmd+Shift+K` rather than the `Ctrl/Cmd+K` older versions of Haste
used, because Unity 6 binds `Ctrl/Cmd+K` to its own Search window (`Edit > Search > Search
All...`). Two commands on one chord means one of them silently never opens, so Haste moved
off it. If you prefer the original chord, take it back in Edit > Shortcuts.

(Additional settings are available in the "Haste" tab of "Unity Preferences".)

Narrowing your search
---

Type more than one word and every word has to match, in any order — `popup crime` finds
`Popup_CrimeScene_Root.prefab`.

Start the query with a prefix to search only one kind of thing. The prefix turns into a
chip; backspace on an empty query clears it.

Filter by type with `t:`, the same syntax Unity's own Project search uses. The chips under
the search field are buttons — click one and it types the tag for you.

Prefix | Searches
---|---
`t:prefab` | prefabs
`t:script` | C# scripts
`t:scene` | scenes
`t:texture` | textures and sprites (`.png`, `.tga`, `.psd`, …)
`t:audio` | audio clips (`.wav`, `.mp3`, `.ogg`, …) — also `t:audioclip`
`t:anim` | animation clips — also `t:animation`
`t:animator` | animator controllers
`t:material` | materials
`t:model` | models (`.fbx`, `.obj`, …)
`t:shader` | shaders
`t:font` | fonts
`t:asset` | anything else in the project
`h:` | GameObjects in the open scenes
`layout:` | saved window layouts
`>` | menu commands
`#` | components

You can also find assets by type simply by searching for their extension:

- `.cs` for C# scripts
- `.unity` for scenes
- `.mat` for materials

Step-By-Step Tutorial
---

##### Step 1. Install Haste into your project (see Installation above).

##### Step 2. Open the included tutorial scene @ `Assets/Tutorial/Tutorial.unity` (available when you clone this repository)

##### Step 3. Press Command+Shift+K (⌘+⇧+K) on macOS (Ctrl+Shift+K on Windows) to open Haste.

This is Haste. You can open it at any time.

The first time you open Haste it will begin indexing your scene hierarchy and project files automatically, making new items available for search as their discovered.

##### Step 4. Type `MyFirstGameObject` into the search box.

Haste will begin listing your search results immediately. Note that searches in Haste are not case-sensitive.

##### Step 5. You can use the up (↑) / down (↓) arrows to navigate the search results. Use the arrows to highlight the GameObject named "first".

##### Step 6. Press Enter (↵) to select the highlighted GameObject.

Pressing enter will select the highlighted result.

Searching by the full name can be tedious. To search faster you can search using Haste's "fuzzy" matching.

##### Step 7. Open Haste and type `msgo`.

Note how Haste highlights the capital letters in the GameObject's name. Haste can search on any "word boundary" which are capital letters or characters that occur after spaces and other characters such as hyphens and parenthesis. Think of this like "keyboard-shortcuts on steroids" where everything in Unity gets an acronym to lookup the object by.

##### Step 8. Press ESC to dismiss Haste.

You can do this at any time when Haste is open without performing any actions.

Next lets use some MenuItem actions...

##### Step 9. Open Haste and search for `TutorialPrefab` (or `tutp` or even `tp`).

This brings up the `TutorialPrefab.prefab` in the project's assets.

##### Step 10. Press Enter (↵) to select the prefab.

##### Step 11. Now search for `Instantiate Prefab` (or `ip`) in Haste and press Enter (↵).

Haste provides access to as many built-in MenuItems as possible with Unity's exposed APIs. Haste also indexes custom MenuItems from other editor extensions making it easy to extend Haste's capabilities.

Ignoring Assets
---

Haste ships with a list of folders it skips: `Assets/Plugins`, the External Dependency
Manager's generated folders, the common mobile SDKs, and Unity's own magic folders. On a
real 20,000-file project that is about 10% of all assets and a quarter of every C# hit — so
a search for "manager" finds your managers rather than a third-party library's.

`Plugins/Android`, `Plugins/iOS` and `Plugins/tvOS` stay searchable, because
`AndroidManifest.xml` and native plugin sources are things you actually look for.

To see exactly what is skipped, or to turn the list off, go to **Preferences > Haste**.

You can add your own, in either of two lists:

- **Shared with the project** — committed to `ProjectSettings/HasteIgnorePaths.asset`, so
  your whole team gets it.
- **Just for you** — stays on your machine.

A rule with a slash is a path (`Assets/Plugins`). A rule without one is a folder name
matched at any depth (`Firebase`). Start a rule with `!` to make an exception, which always
wins.

You can also right-click a folder and choose `Haste > Ignore` (or `Unignore`), which adds
it to your personal list.

Menu Items
---

Haste indexes the editor's live menu tree, so it finds whatever your editor actually has —
built-in menus, menus added by packages, and your own `[MenuItem]` methods, including ones
under a menu of your own invention. Nothing is hardcoded, so there is no list here to go
stale, and Haste will not offer you a menu item that does not exist.

Older versions shipped a fixed list of menu paths captured from Unity 5. On Unity 6 nearly
half of those paths no longer existed, and several hundred real menu items were missing.

Two things are still out of reach, because they are not part of the menu tree the editor
exposes: the macOS application menu (`About Unity`, `Settings…`) and the project wizard
(`New Project…`, `Open Project…`).
