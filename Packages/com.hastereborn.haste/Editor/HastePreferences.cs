using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Haste {

  public static class HastePreferences {

    static Vector2 scrollPosition = Vector2.zero;

    // Foldout state. Note there is no GUIStyle here at all any more: HasteStyles used to
    // build twenty-five of them plus a light/dark colour matrix, behind an EditorStyles
    // readiness gate at startup, and the last survivor was a word-wrapping label for the
    // paragraphs this page used to print. Do not bring it back. A control that needs
    // explaining gets a tooltip, which needs no style and does not push the next control
    // off the screen.
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

    // One line each, and a tooltip instead of a paragraph.
    //
    // This page used to carry four multi-paragraph HelpBoxes -- most of a screen of prose
    // between the reader and the controls. The rule now is that a control explains itself
    // in its tooltip and the page stays scannable; anything that genuinely needs three
    // paragraphs belongs in the README, not here.
    static void Section(string title) {
      EditorGUILayout.Space();
      EditorGUILayout.Space();
      EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
      EditorGUILayout.Space();
    }

    static GUIContent Label(string text, string tooltip) {
      return new GUIContent(text, tooltip);
    }

    public static void PreferencesGUI() {
      using (var scrollView = new HasteScrollView(scrollPosition)) {
        scrollPosition = scrollView.ScrollPosition;

        EditorGUILayout.Space();

        // Version and usage in one line rather than a section apiece.
        EditorGUILayout.LabelField(String.Format(
          "Haste {0}  \u00b7  opened {1:N0} times since {2}, about {3:N0} a day",
          Haste.VERSION, HasteSettings.UsageCount,
          HasteSettings.UsageSinceDate.ToShortDateString(), HasteSettings.UsageAverage),
          EditorStyles.miniLabel);

        // ------------------------------------------------------------------ sources
        Section("Search Sources");

        using (var toggleGroup = new HasteToggleGroup("Enabled", HasteSettings.Enabled)) {
          HasteSettings.Enabled = toggleGroup.Enabled;
          EditorGUILayout.Space();

          foreach (var watcher in Haste.Watchers) {
            var label = Label(
              String.Format("{0} ({1:N0})", watcher.Key, watcher.Value.IndexedCount),
              "Include " + watcher.Key + " results. The number is how many are indexed.");

            var watchedEnabled = EditorGUILayout.Toggle(label, watcher.Value.Enabled);
            if (watchedEnabled != watcher.Value.Enabled) {
              EditorPrefs.SetBool(HasteSettings.GetPrefKey(HasteSetting.Source, watcher.Key), watchedEnabled);
              Haste.Watchers.ToggleSource(watcher.Key, watchedEnabled);
            }
          }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
          Label("Indexed", "Everything Haste can currently search."),
          new GUIContent(Haste.IndexedCount.ToString("N0")));

        EditorGUILayout.Space();
        if (GUILayout.Button(Label("Rebuild Index",
              "Index everything again. Use this if results look stale."),
              GUILayout.Width(128))) {
          Haste.Rebuild();
        }

        // ------------------------------------------------------------------ opening
        Section("Opening Haste");

        EditorGUILayout.LabelField(
          Label("Shortcut", "Rebind it under Edit > Shortcuts, like any other."),
          new GUIContent(Application.platform == RuntimePlatform.OSXEditor
            ? "\u2318\u21e7K" : "Ctrl+Shift+K"));

        var doubleTap = EditorGUILayout.Toggle(
          Label("Double-tap Shift",
            "Tap Shift twice quickly to open Haste. Ignored while typing, dragging, " +
            "in play mode, and while indexing."),
          HasteSettings.DoubleTapShiftEnabled);
        if (doubleTap != HasteSettings.DoubleTapShiftEnabled) {
          HasteSettings.DoubleTapShiftEnabled = doubleTap;
        }

        using (new HasteDisabled(!doubleTap)) {
          var window = EditorGUILayout.IntSlider(
            Label("Tap window (ms)", "How long between the two taps still counts as one gesture."),
            HasteSettings.DoubleTapShiftWindowMs, 120, 600);
          if (window != HasteSettings.DoubleTapShiftWindowMs) {
            HasteSettings.DoubleTapShiftWindowMs = window;
          }

          var diagnostics = EditorGUILayout.Toggle(
            Label("Log key events", "Writes every key Haste sees to the console."),
            HasteSettings.DoubleTapShiftDiagnostics);
          if (diagnostics != HasteSettings.DoubleTapShiftDiagnostics) {
            HasteSettings.DoubleTapShiftDiagnostics = diagnostics;
          }
        }

        // Conditional and genuinely surprising, so it stays a box -- but one line of it.
        if (HasteDoubleTapShift.IsDisabled) {
          EditorGUILayout.Space();
          EditorGUILayout.HelpBox(
            "Double-tap Shift switched itself off this session. The shortcut still works.",
            MessageType.Warning);
          if (GUILayout.Button("Reset double-tap state", GUILayout.Width(180))) {
            HasteDoubleTapShift.ResetState();
          }
        }

        // ------------------------------------------------------------------ ignoring
        Section("Ignored Paths");
        HasteIgnore.DrawPreferences();

        // ------------------------------------------------------------------ ranking
        Section("Ranking");

        showWeights = EditorGUILayout.Foldout(showWeights, Label("Weights by type",
          "Multiplies a type's score after matching. 1 is neutral, 0 sinks it."));
        if (showWeights) {
          foreach (var kind in HasteKinds.All) {
            // Menu items are weighted by their root, below. A slider here would be a
            // control that silently does nothing.
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
          if (GUILayout.Button("Reset", GUILayout.Width(128))) {
            HasteWeights.ResetToDefaults();
          }
          EditorGUILayout.Space();
        }

        showMenuWeights = EditorGUILayout.Foldout(showMenuWeights, Label("Weights by menu",
          "Unity's own menus start at " + HasteMenuWeights.BuiltinDefault +
          "; menus this project added start at " + HasteMenuWeights.DiscoveredDefault + "."));
        if (showMenuWeights) {
          var wasBuiltin = true;

          foreach (var root in MenuRoots) {
            var builtin = HasteMenuItemSource.IsBuiltinRoot(root);

            // The editor's menus come first and the project's follow. The break between
            // them is the whole reason this list exists, so it is drawn.
            if (wasBuiltin && !builtin) {
              EditorGUILayout.Space();
              EditorGUILayout.LabelField("From this project", EditorStyles.miniLabel);
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
          if (GUILayout.Button("Reset", GUILayout.Width(128))) {
            HasteMenuWeights.ResetToDefaults();
          }
          EditorGUILayout.Space();
        }

        var favorites = HasteFavorites.instance.ToArray();

        showFavorites = EditorGUILayout.Foldout(showFavorites, Label(
          favorites.Length == 0 ? "Favorites" : "Favorites (" + favorites.Length + ")",
          "Alt+Enter on a row. A favorite scores " + HasteFavorites.Multiplier +
          "\u00d7 and shows a star. Scene objects cannot be favorited."));

        if (showFavorites) {
          if (favorites.Length == 0) {
            EditorGUILayout.LabelField("Nothing yet.", EditorStyles.miniLabel);
          } else {
            // Collected rather than removed inside the loop: mutating the list being
            // drawn throws out of the middle of a layout group.
            string remove = null;

            foreach (var key in favorites) {
              EditorGUILayout.BeginHorizontal();
              EditorGUILayout.LabelField(new GUIContent(
                HasteFavorites.PathOf(key),
                HasteFavorites.SourceOf(key) + "  \u2014  " + HasteFavorites.PathOf(key)));
              if (GUILayout.Button(new GUIContent("\u00d7", "Remove"), GUILayout.Width(22))) {
                remove = key;
              }
              EditorGUILayout.EndHorizontal();
            }

            if (remove != null) {
              HasteFavorites.instance.RemoveKey(remove);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Clear", GUILayout.Width(128))) {
              HasteFavorites.instance.Clear();
            }
          }
        }

        // ------------------------------------------------------------------ browsing
        Section("Browsing");

        var selectEnabled = EditorGUILayout.Toggle(
          Label("Select as you move",
            "Selects each result as you arrow past it. Expands folders as it goes."),
          HasteSettings.SelectEnabled);
        if (selectEnabled != HasteSettings.SelectEnabled) {
          HasteSettings.SelectEnabled = selectEnabled;
        }

        EditorGUILayout.Space();
      }
    }
  }
}
