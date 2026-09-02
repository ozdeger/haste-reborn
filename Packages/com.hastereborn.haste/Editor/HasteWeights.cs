using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // A per-kind multiplier applied to every search score.
  //
  // Ranking answers "how well does this match?", which is not the same question as "how
  // likely is this to be what you wanted". A menu command whose name matches perfectly is
  // still usually less wanted than a prefab that matches nearly as well -- and there are
  // 529 menu items in a stock Unity 6 editor, enough to bury a project's own assets.
  // These weights are the second question, kept separate from the first.
  //
  // Per-user rather than per-project, deliberately: this is a preference about how someone
  // searches, not a fact about the project. It lives in EditorPrefs alongside the other
  // personal settings, not in the shared ignore list.
  public static class HasteWeights {

    public const float Min = 0.0f;
    public const float Max = 2.0f;

    // Anything not listed weighs 1.0. Only the demotions are stated, so the table reads as
    // "what gets pushed down" rather than a wall of 1.0s.
    static readonly Dictionary<HasteKind, float> defaults = new Dictionary<HasteKind, float> {
      // Scene objects, including whatever is open in prefab mode. Numerous, transient, and
      // rarely what a search is for -- the strongest demotion.
      { HasteKind.Hierarchy, 0.5f },

      // Saved window layouts. Few, but almost never what a search is for.
      { HasteKind.Layout,    0.7f },

      // Menu, Tool and Component are all menu items and are NOT weighted here -- see
      // HasteMenuWeights, which weights them by menu root instead so that a project's own
      // tools menu can sit above Unity's 529 stock commands. They stay in HasteKind
      // because scope tokens ("t:menu") still classify by them.
    };

    static Dictionary<HasteKind, float> cache;

    public static float Default(HasteKind kind) {
      float weight;
      return defaults.TryGetValue(kind, out weight) ? weight : 1.0f;
    }

    public static float Get(HasteKind kind) {
      float weight;
      return Cache.TryGetValue(kind, out weight) ? weight : 1.0f;
    }

    public static void Set(HasteKind kind, float weight) {
      weight = Mathf.Clamp(weight, Min, Max);
      Cache[kind] = weight;
      EditorPrefs.SetFloat(PrefKey(kind), weight);
    }

    public static void ResetToDefaults() {
      foreach (var kind in HasteKinds.All) {
        EditorPrefs.DeleteKey(PrefKey(kind));
      }
      cache = null;
    }

    public static float For(HasteItem item) {
      if (item != null && item.source == HasteMenuItemSource.NAME) {
        return HasteMenuWeights.For(item);
      }
      return Get(HasteKinds.Classify(item));
    }

    // Kinds whose weight comes from the menu root rather than this table. Preferences
    // hides them so there is no slider that silently does nothing.
    public static bool IsMenuDriven(HasteKind kind) {
      return kind == HasteKind.Menu || kind == HasteKind.Tool || kind == HasteKind.Component;
    }

    // Read once per domain rather than per item. Map applies this to every match, and
    // EditorPrefs is a native call -- doing it per result would put a p/invoke in the
    // middle of the search loop.
    static Dictionary<HasteKind, float> Cache {
      get {
        if (cache == null) {
          cache = new Dictionary<HasteKind, float>(HasteKinds.All.Length);
          foreach (var kind in HasteKinds.All) {
            cache[kind] = EditorPrefs.GetFloat(PrefKey(kind), Default(kind));
          }
        }
        return cache;
      }
    }

    static string PrefKey(HasteKind kind) {
      return HasteSettings.GetPrefKey(HasteSetting.Weight, kind.ToString());
    }
  }
}
