using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Haste {

  class IgnorePathsProxy : ScriptableObject {

    [HideInInspector]
    public List<string> paths;

    public int Length {
      get {
        return paths.Count;
      }
    }

    public void Add(string path) {
      paths.Add(path);
    }

    public void Remove(string path) {
      paths.Remove(path);
    }

    public void RemoveAt(int index) {
      paths.RemoveAt(index);
    }

    public string this[int index] {
      get {
        return paths[index];
      }
      set {
        paths[index] = value;
      }
    }

    public bool Contains(string path) {
      return paths.Contains(path);
    }

    // Set by whichever list this proxy is backing.
    public Action<List<string>> onSave;

    public void Save() {
      onSave(paths);
      Haste.Rebuild();
    }

    public IgnorePathsProxy Init(IEnumerable<string> initial, Action<List<string>> save) {
      paths = initial.ToList();
      onSave = save;
      return this;
    }
  }

  public static class HasteIgnore {

    // Everything the Project source skips: the shipped list, the project's shared list,
    // and the user's own additions.
    public static IList<string> EffectiveRules() {
      var rules = new List<string>();

      if (HasteSettings.UseRecommendedIgnores) {
        rules.AddRange(HasteIgnoreRules.Builtin);
      }

      rules.AddRange(HasteIgnorePaths.instance.Paths);
      rules.AddRange(HasteIgnoreRules.Parse(HasteSettings.IgnorePaths));

      return rules;
    }

    // Whether anything below `path` is excepted, so the crawler knows an ignored folder
    // still has to be walked. Without this, "!Assets/Plugins/Android" could never match:
    // the walk would stop at Assets/Plugins and never reach it.
    public static bool HasExceptionUnder(string path, IList<string> rules) {
      var prefix = path + "/";

      for (int i = 0; i < rules.Count; i++) {
        var rule = rules[i];
        if (string.IsNullOrEmpty(rule) || rule[0] != HasteIgnoreRules.NegationPrefix) {
          continue;
        }

        var body = rule.Substring(1).Trim();
        if (body.Length == 0) {
          continue;
        }

        // A bare folder name could be anywhere below, so it always forces the walk.
        if (body.IndexOf('/') < 0) {
          return true;
        }

        if (body.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }

      return false;
    }

    static IgnorePathsProxy sharedProxy;
    static IgnorePathsProxy SharedProxy {
      get {
        if (sharedProxy == null) {
          sharedProxy = ScriptableObject.CreateInstance<IgnorePathsProxy>()
            .Init(HasteIgnorePaths.instance.Paths, saved => {
              HasteIgnorePaths.instance.Paths.Clear();
              HasteIgnorePaths.instance.Paths.AddRange(saved);
              HasteIgnorePaths.instance.Commit();
            });
          sharedProxy.hideFlags = HideFlags.HideAndDontSave;
        }
        return sharedProxy;
      }
    }

    static IgnorePathsProxy personalProxy;
    static IgnorePathsProxy PersonalProxy {
      get {
        if (personalProxy == null) {
          personalProxy = ScriptableObject.CreateInstance<IgnorePathsProxy>()
            .Init(HasteIgnoreRules.Parse(HasteSettings.IgnorePaths),
                  saved => HasteSettings.IgnorePaths = string.Join(",", saved.ToArray()));
          personalProxy.hideFlags = HideFlags.HideAndDontSave;
        }
        return personalProxy;
      }
    }

    static ReorderableList BuildList(IgnorePathsProxy proxy, string header) {
      var list = new ReorderableList(proxy.paths, typeof(IgnorePathsProxy), true, true, true, true);
      list.drawHeaderCallback += rect => EditorGUI.LabelField(rect, header);
      list.drawElementCallback += (rect, index, active, focused) => {
        if (index >= 0 && index < proxy.Length) {
          EditorGUI.BeginChangeCheck();
          rect = new Rect(rect.x, rect.y + 1, rect.width, rect.height - 4);
          proxy[index] = EditorGUI.TextField(rect, proxy[index]);
          if (EditorGUI.EndChangeCheck()) {
            EditorUtility.SetDirty(proxy);
          }
        }
      };
      list.onAddCallback += _ => { proxy.Add(""); EditorUtility.SetDirty(proxy); };
      list.onRemoveCallback += l => { proxy.RemoveAt(l.index); EditorUtility.SetDirty(proxy); };
      return list;
    }

    static ReorderableList sharedList;
    static ReorderableList SharedList {
      get { return sharedList ?? (sharedList = BuildList(SharedProxy, "Shared with the project")); }
    }

    static ReorderableList personalList;
    static ReorderableList PersonalList {
      get { return personalList ?? (personalList = BuildList(PersonalProxy, "Just for you")); }
    }

    static bool showBuiltin;

    public static void DrawPreferences() {
      var useBuiltin = EditorGUILayout.Toggle(new GUIContent("Use recommended",
        "Skip Plugins, vendored SDKs and Unity's generated folders."),
        HasteSettings.UseRecommendedIgnores);
      if (useBuiltin != HasteSettings.UseRecommendedIgnores) {
        HasteSettings.UseRecommendedIgnores = useBuiltin;
        Haste.Rebuild();
      }

      // Shown rather than merely counted: this list hides results silently, so the one
      // place it is documented had better be the place you go when something is missing.
      showBuiltin = EditorGUILayout.Foldout(showBuiltin, new GUIContent(
        string.Format("Recommended ({0})", HasteIgnoreRules.Builtin.Length),
        "What the setting above skips. Read-only."));
      if (showBuiltin) {
        using (new HasteDisabled(true)) {
          foreach (var rule in HasteIgnoreRules.Builtin) {
            EditorGUILayout.LabelField("    " + rule);
          }
        }
      }

      EditorGUILayout.Space();
      EditorGUILayout.LabelField(new GUIContent("Shared with the project",
        "Committed in ProjectSettings/HasteIgnorePaths.asset, so your team gets it."));
      SharedList.DoLayoutList();
      if (GUILayout.Button("Save", GUILayout.Width(128))) {
        SharedProxy.Save();
      }

      EditorGUILayout.Space();
      EditorGUILayout.LabelField(new GUIContent("Just for you",
        "Stays on this machine. Right-clicking a folder > Haste > Ignore adds it here."));
      PersonalList.DoLayoutList();
      if (GUILayout.Button("Save", GUILayout.Width(128))) {
        PersonalProxy.Save();
      }

      EditorGUILayout.Space();
      EditorGUILayout.LabelField(
        "A slash makes it a path; no slash matches that folder name at any depth; ! excepts.",
        EditorStyles.miniLabel);
    }

    [MenuItem("Assets/Haste/Ignore")]
    public static void Ignore() {
      var path = AssetDatabase.GetAssetPath(Selection.activeObject);
      PersonalProxy.Add(path);
      PersonalProxy.Save();
    }

    [MenuItem("Assets/Haste/Ignore", true)]
    public static bool CanIgnore() {
      var selection = Selection.activeObject;
      if (selection == null) {
        return false;
      }
      if (!AssetDatabase.Contains(selection)) {
        return false; // invalid asset
      }
      var path = AssetDatabase.GetAssetPath(selection);
      if (!Directory.Exists(path)) {
        return false; // invalid directory
      }
      if (PersonalProxy.Contains(path)) {
        return false; // already ignored
      }
      return true;
    }

    [MenuItem("Assets/Haste/Unignore")]
    public static void Unignore() {
      var path = AssetDatabase.GetAssetPath(Selection.activeObject);
      PersonalProxy.Remove(path);
      PersonalProxy.Save();
    }

    [MenuItem("Assets/Haste/Unignore", true)]
    public static bool CanUnignore() {
      var selection = Selection.activeObject;
      if (selection == null) {
        return false;
      }
      if (!AssetDatabase.Contains(selection)) {
        return false; // invalid asset
      }
      var path = AssetDatabase.GetAssetPath(selection);
      if (!Directory.Exists(path)) {
        return false; // invalid directory
      }
      if (!PersonalProxy.Contains(path)) {
        return false; // not ignored
      }
      return true;
    }
  }
}
