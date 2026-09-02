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

        EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        bool selectEnabled = EditorGUILayout.Toggle("Enable Select", HasteSettings.SelectEnabled);
        if (selectEnabled != HasteSettings.SelectEnabled) {
          HasteSettings.SelectEnabled = selectEnabled;
        }
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("By default, Haste will temporarily select results as you scroll through them. Disabling this feature prevents the expansion of the hierarchy and project folders during search.", MessageType.Info);

        EditorGUILayout.Space();
      }
    }
  }
}
