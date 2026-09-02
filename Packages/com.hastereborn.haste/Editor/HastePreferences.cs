using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Haste {

  public static class HastePreferences {

    static Vector2 scrollPosition = Vector2.zero;

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
        ), HasteStyles.GetStyle("Usage"));

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

        EditorGUILayout.LabelField("Window Position", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        bool showHandle = EditorGUILayout.Toggle("Enable Moving Window", HasteSettings.ShowHandle);
        if (showHandle != HasteSettings.ShowHandle) {
          HasteSettings.ShowHandle = showHandle;

          // If we are showing the handle, open window
          if (showHandle) {
            HasteWindow.Open();

          // If the window is open and we turned off the handle, close window
          } else if (HasteWindow.IsOpen) {
            HasteWindow.Instance.Close();
          }
        }
        EditorGUILayout.Space();

        using (new HasteHorizontal()) {
          using (new HasteDisabled(!showHandle || !HasteWindow.IsOpen)) {
            if (GUILayout.Button("Save Window Position", GUILayout.Width(128))) {
              HasteSettings.WindowPosition = HasteWindow.Instance.position.position;

              // Close window after saving its position.
              if (HasteWindow.IsOpen) {
                HasteWindow.Instance.Close();
              }

              HasteSettings.ShowHandle = false;
            }
          }

          using (new HasteDisabled(HasteSettings.WindowPosition == Vector2.zero)) {
            if (GUILayout.Button("Reset Window Position", GUILayout.Width(128))) {
              HasteSettings.WindowPosition = Vector2.zero;

              // Close window after restoring its position.
              if (HasteWindow.IsOpen) {
                HasteWindow.Instance.Close();
              }
            }
          }
        }

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
