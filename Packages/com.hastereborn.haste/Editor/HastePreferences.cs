using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Haste {

  public static class HastePreferences {

    static Vector2 scrollPosition = Vector2.zero;

    // The preferences page is the last IMGUI surface in Haste, and this is the only style
    // it needs. HasteStyles used to build twenty-five of them, plus a light/dark colour
    // matrix, behind an EditorStyles readiness gate at startup -- all to serve this one
    // label once the palette moved to USS.
    //
    // Built lazily rather than in a static initialiser: EditorStyles is unusable outside
    // an interactive editor, and guiHandler is the first point where it is safe to read.
    static bool showWeights;
    static bool showMenuWeights;
    static bool showFavorites;

    // Built once per domain: reading it walks every loaded assembly looking for
    // [MenuItem], which is ~120 ms, and OnGUI runs on every repaint. A new menu root
    // requires a script compile, and that reloads the domain and clears this.
    static string[] menuRoots;

    static string[] MenuRoots {
      get {
        if (menuRoots == null) {
          menuRoots = HasteMenuItemSource.Roots;
        }
        return menuRoots;
      }
    }

    static GUIStyle wrappedLabel;

    static GUIStyle WrappedLabel {
      get {
        if (wrappedLabel == null) {
          wrappedLabel = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }
        return wrappedLabel;
      }
    }

    // Where Haste appears in Unity Preferences. SettingsProvider keys user overrides and
    // the settings search index by this path, so renaming it moves the page.
    public const string SettingsPath = "Preferences/Haste";

    // Replaces [PreferenceItem("Haste")], deprecated in favour of [SettingsProvider].
    //
    // Same place in the UI -- SettingsScope.User is the Preferences window -- but the
    // page is now searchable, which [PreferenceItem] pages never were. The keywords are
    // what the settings search box matches on.
    [SettingsProvider]
    public static SettingsProvider CreateSettingsProvider() {
      return new SettingsProvider(SettingsPath, SettingsScope.User) {
        label = "Haste",
        guiHandler = searchContext => PreferencesGUI(),
        keywords = new HashSet<string>(new[] {
          "haste", "search", "fuzzy", "palette", "index", "sources",
          "hierarchy", "project", "menu item", "layout", "ignore", "shortcut",
        }),
      };
    }

    public static void PreferencesGUI() {
      using (var scrollView = new HasteScrollView(scrollPosition)) {
        scrollPosition = scrollView.ScrollPosition;

        EditorGUILayout.Space();

        EditorGUILayout.LabelField(String.Format("Haste has been opened {0:N0} times since {1} (about {2:N0} times per day).",
          HasteSettings.UsageCount,
          HasteSettings.UsageSinceDate.ToLongDateString(),
          HasteSettings.UsageAverage
        ), WrappedLabel);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Version", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Current Version", Haste.VERSION);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Available Sources", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (var toggleGroup = new HasteToggleGroup("Haste Enabled", HasteSettings.Enabled)) {
          HasteSettings.Enabled = toggleGroup.Enabled;
          EditorGUILayout.Space();

          foreach (var watcher in Haste.Watchers) {
            string label = System.String.Format("{0} ({1})", watcher.Key, watcher.Value.IndexedCount);
            bool watchedEnabled = EditorGUILayout.Toggle(label, watcher.Value.Enabled);
            if (watchedEnabled != watcher.Value.Enabled) {
              EditorPrefs.SetBool(HasteSettings.GetPrefKey(HasteSetting.Source, watcher.Key), watchedEnabled);
              Haste.Watchers.ToggleSource(watcher.Key, watchedEnabled);
            }
          }
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Indexed Count", Haste.IndexedCount.ToString());

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        HasteIgnore.DrawPreferences();

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        if (GUILayout.Button("Rebuild Index", GUILayout.Width(128))) {
          Haste.Rebuild();
        }
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Rebuilds the internal index used for fast searching in Haste. Use this if Haste starts providing weird results.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Opening Haste", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Shortcut", Application.platform == RuntimePlatform.OSXEditor
          ? "\u2318\u21e7K  (rebind in Edit > Shortcuts)"
          : "Ctrl+Shift+K  (rebind in Edit > Shortcuts)");
        EditorGUILayout.Space();

        var doubleTap = EditorGUILayout.Toggle("Double-tap Shift", HasteSettings.DoubleTapShiftEnabled);
        if (doubleTap != HasteSettings.DoubleTapShiftEnabled) {
          HasteSettings.DoubleTapShiftEnabled = doubleTap;
        }

        using (new HasteDisabled(!doubleTap)) {
          var window = EditorGUILayout.IntSlider("Tap window (ms)",
            HasteSettings.DoubleTapShiftWindowMs, 120, 600);
          if (window != HasteSettings.DoubleTapShiftWindowMs) {
            HasteSettings.DoubleTapShiftWindowMs = window;
          }

          var diagnostics = EditorGUILayout.Toggle("Log key events", HasteSettings.DoubleTapShiftDiagnostics);
          if (diagnostics != HasteSettings.DoubleTapShiftDiagnostics) {
            HasteSettings.DoubleTapShiftDiagnostics = diagnostics;
          }
        }

        if (HasteDoubleTapShift.IsDisabled) {
          EditorGUILayout.Space();
          EditorGUILayout.HelpBox(
            "Double-tap Shift switched itself off this session, either because it fired " +
            "repeatedly or because the editor hook failed. The keyboard shortcut is " +
            "unaffected.", MessageType.Warning);
          if (GUILayout.Button("Reset double-tap state", GUILayout.Width(180))) {
            HasteDoubleTapShift.ResetState();
          }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
          "Tap Shift twice, quickly, to open Haste. It is ignored while you are typing in " +
          "a field, while dragging, in play mode, and while Haste is indexing.\n\n" +
          "This cannot live in Edit > Shortcuts \u2014 Unity's shortcut system rejects " +
          "modifier-only bindings \u2014 so the tap window is tuned here instead. The " +
          "keyboard shortcut above always works regardless.\n\n" +
          "\"Log key events\" writes every key Haste sees to the console. Turn it on only " +
          "if the gesture is not firing and you want to report what your keyboard sends.",
          MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Result Weights", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        showWeights = EditorGUILayout.Foldout(showWeights, "Weights by type");
        if (showWeights) {
          foreach (var kind in HasteKinds.All) {
            // Menu items are weighted by their root, below. Showing a slider here too
            // would be a control that silently does nothing.
            if (HasteWeights.IsMenuDriven(kind)) {
              continue;
            }

            var current = HasteWeights.Get(kind);
            var updated = EditorGUILayout.Slider(
              ObjectNames.NicifyVariableName(kind.ToString()),
              current, HasteWeights.Min, HasteWeights.Max);
            if (!Mathf.Approximately(updated, current)) {
              HasteWeights.Set(kind, updated);
            }
          }

          EditorGUILayout.Space();
          if (GUILayout.Button("Reset Weights", GUILayout.Width(128))) {
            HasteWeights.ResetToDefaults();
          }
        }

        EditorGUILayout.Space();

        showMenuWeights = EditorGUILayout.Foldout(showMenuWeights, "Weights by menu");
        if (showMenuWeights) {
          var wasBuiltin = true;

          foreach (var root in MenuRoots) {
            var builtin = HasteMenuItemSource.IsBuiltinRoot(root);

            // The editor's menus come first and the project's follow. The break between
            // them is the whole reason this list exists, so it is drawn.
            if (wasBuiltin && !builtin) {
              EditorGUILayout.Space();
              EditorGUILayout.LabelField("From this project and its packages",
                EditorStyles.miniLabel);
            }
            wasBuiltin = builtin;

            var current = HasteMenuWeights.Get(root);
            var updated = EditorGUILayout.Slider(root, current,
              HasteMenuWeights.Min, HasteMenuWeights.Max);
            if (!Mathf.Approximately(updated, current)) {
              HasteMenuWeights.Set(root, updated);
            }
          }

          EditorGUILayout.Space();
          if (GUILayout.Button("Reset Menu Weights", GUILayout.Width(148))) {
            HasteMenuWeights.ResetToDefaults();
          }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
          "Multiplies the score of every result of that type, after matching. Use it to " +
          "push whole categories down without hiding them \u2014 scene objects start " +
          "below 1 because there are a great many of them and they match short queries " +
          "readily.\n\n" +
          "Menu items are weighted by their menu instead, because they are not all alike: " +
          "the ~529 commands Unity ships start at " + HasteMenuWeights.BuiltinDefault +
          ", while a menu this project added \u2014 your own tools \u2014 starts at " +
          HasteMenuWeights.DiscoveredDefault + " and is listed as soon as it is found.\n\n" +
          "1 leaves a type where the match quality puts it. 0 sinks it to the bottom; to " +
          "remove a type from results entirely, turn its source off above instead.\n\n" +
          "These are yours, not the project's \u2014 they stay on this machine.",
          MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Favorites", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        var favorites = HasteFavorites.instance.ToArray();

        showFavorites = EditorGUILayout.Foldout(showFavorites,
          favorites.Length == 0 ? "Favorites" : "Favorites (" + favorites.Length + ")");

        if (showFavorites) {
          if (favorites.Length == 0) {
            EditorGUILayout.LabelField(
              "Nothing yet. Press Alt+Enter on a row in Haste, or right-click an asset " +
              "and choose Haste > Add to Favorites.", WrappedLabel);
          } else {
            // Collected rather than removed inside the loop: mutating the list being
            // drawn throws out of the middle of a layout group.
            string remove = null;

            foreach (var key in favorites) {
              EditorGUILayout.BeginHorizontal();
              EditorGUILayout.LabelField(new GUIContent(
                HasteFavorites.PathOf(key),
                HasteFavorites.SourceOf(key) + "  \u2014  " + HasteFavorites.PathOf(key)));
              if (GUILayout.Button("\u00d7", GUILayout.Width(22))) {
                remove = key;
              }
              EditorGUILayout.EndHorizontal();
            }

            if (remove != null) {
              HasteFavorites.instance.RemoveKey(remove);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Favorites", GUILayout.Width(128))) {
              HasteFavorites.instance.Clear();
            }
          }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
          "A favorite scores " + HasteFavorites.Multiplier + "\u00d7 its usual score, on " +
          "top of every other weight, and its row shows a star.\n\n" +
          "Scene objects cannot be favorited. A favorite is remembered by path, and a " +
          "GameObject's path changes when it is renamed, reparented or its scene is " +
          "closed \u2014 a favorite that silently stops matching is worse than not " +
          "offering one.\n\n" +
          "These live in this project's UserSettings folder: yours, and not committed.",
          MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        bool selectEnabled = EditorGUILayout.Toggle("Enable Select", HasteSettings.SelectEnabled);
        if (selectEnabled != HasteSettings.SelectEnabled) {
          HasteSettings.SelectEnabled = selectEnabled;
        }
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Temporarily selects each result as you scroll through it, so you can see it in the editor. Off by default, because selecting expands hierarchy and project folders as it goes.", MessageType.Info);

        EditorGUILayout.Space();
      }
    }
  }
}
