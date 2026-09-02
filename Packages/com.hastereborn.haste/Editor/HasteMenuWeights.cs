using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // A per-menu-root multiplier: the menu half of HasteWeights.
  //
  // One weight for "menu items" was too blunt. Unity ships ~529 of them across the seven
  // roots it owns, and demoting all of those is right. But a project's own tooling arrives
  // through the same source, under roots like "Tools" or "Dev Tools", and that is usually
  // the opposite of noise -- it is the thing worth a shortcut. A single weight forced a
  // choice between burying the tools you wrote and surfacing every stock menu command.
  //
  // So roots are weighted separately, and the split is drawn where the editor itself draws
  // it: HasteMenuItemSource.BuiltinRoots is the menu bar Unity ships, which has to be
  // named because there is no root enumeration API. Every OTHER root is known only because
  // some [MenuItem] in this project or its packages invented it -- which is a good proxy
  // for "someone here cares about this". The first group starts demoted; the second starts
  // at 1.0 and appears in preferences as soon as it is discovered.
  public static class HasteMenuWeights {

    // Matches what HasteWeights used to apply to every menu item, so the editor's own
    // menus rank exactly as they did before they were split apart.
    public const float BuiltinDefault = 0.7f;

    // A root that is not Unity's exists because this project put it there.
    public const float DiscoveredDefault = 1.0f;

    public static float Min { get { return HasteWeights.Min; } }
    public static float Max { get { return HasteWeights.Max; } }

    static Dictionary<string, float> cache;

    // The menu root a path sits under. A top-level item with no separator is its own root,
    // which is what the menu bar shows.
    //
    // Returns the interned constant for the editor's own roots, so the common case -- the
    // ~500 stock items, scored on every keystroke -- allocates nothing.
    public static string RootOf(string path) {
      var builtin = HasteMenuItemSource.MatchBuiltinRoot(path);
      if (builtin != null) {
        return builtin;
      }

      if (string.IsNullOrEmpty(path)) {
        return string.Empty;
      }

      var separator = path.IndexOf('/');
      if (separator < 0) {
        return path;
      }
      if (separator == 0) {
        return string.Empty;
      }
      return path.Substring(0, separator);
    }

    public static float Default(string root) {
      return HasteMenuItemSource.IsBuiltinRoot(root) ? BuiltinDefault : DiscoveredDefault;
    }

    // Read once per root rather than per item: Map applies this to every match and
    // EditorPrefs is a native call, so an uncached lookup would put a p/invoke in the
    // middle of the search loop. Ordinal comparison for the same reason HasteScoring uses
    // it -- these are menu paths, not prose.
    public static float Get(string root) {
      if (string.IsNullOrEmpty(root)) {
        return DiscoveredDefault;
      }

      if (cache == null) {
        cache = new Dictionary<string, float>(StringComparer.Ordinal);
      }

      float weight;
      if (!cache.TryGetValue(root, out weight)) {
        weight = EditorPrefs.GetFloat(PrefKey(root), Default(root));
        cache[root] = weight;
      }
      return weight;
    }

    public static void Set(string root, float weight) {
      if (string.IsNullOrEmpty(root)) {
        return;
      }

      weight = Mathf.Clamp(weight, Min, Max);

      if (cache == null) {
        cache = new Dictionary<string, float>(StringComparer.Ordinal);
      }
      cache[root] = weight;
      EditorPrefs.SetFloat(PrefKey(root), weight);
    }

    public static void ResetToDefaults() {
      // Both the roots that exist now and anything already read this session -- a root can
      // disappear when a package is removed, and its stored weight should go with it
      // rather than lie in wait for a package of the same name.
      var roots = new HashSet<string>(HasteMenuItemSource.Roots, StringComparer.Ordinal);
      if (cache != null) {
        foreach (var root in cache.Keys) {
          roots.Add(root);
        }
      }

      foreach (var root in roots) {
        EditorPrefs.DeleteKey(PrefKey(root));
      }

      cache = null;
    }

    public static float For(HasteItem item) {
      return item == null ? DiscoveredDefault : Get(RootOf(item.path));
    }

    // A separate setting from HasteSetting.Weight on purpose. Both are suffixed with a
    // name, and "Component" is BOTH a HasteKind and a menu root -- sharing the prefix
    // would have made the kind weight and the menu weight the same stored value.
    static string PrefKey(string root) {
      return HasteSettings.GetPrefKey(HasteSetting.MenuWeight, root);
    }
  }
}
