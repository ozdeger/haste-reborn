using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // The editor's own icon for a kind, for rows that have no asset to take one from.
  //
  // Project rows get AssetDatabase.GetCachedIcon and hierarchy rows get ObjectContent, so
  // they already show the icon the user knows. Menu items and window layouts are not
  // assets and have no object, so they used to fall back to a text badge -- "MENU", "LAY"
  // -- which is legible but reads as a different class of thing than every row around it.
  //
  // Names are Unity's built-in icon names, resolved through EditorGUIUtility.IconContent
  // so the light/dark variant is picked automatically (it prefixes "d_" on the dark skin;
  // do not write that prefix here). Every name in this table is asserted to resolve by
  // HasteIconTests -- these are internal resource names with no compile-time check, so a
  // typo or a rename in a future editor would otherwise show up as a silently blank icon.
  public static class HasteIcons {

    static readonly Dictionary<HasteKind, string> names = new Dictionary<HasteKind, string> {
      // The kinds that actually reach this fallback.
      { HasteKind.Menu,      "GUISkin Icon" },
      { HasteKind.Component, "Collab.Build" },
      { HasteKind.Layout,    "Layout" },

      // Reached only when the lookups above fail -- a hierarchy row whose object has been
      // destroyed, or an asset the Project window has no icon for.
      { HasteKind.Hierarchy, "GameObject Icon" },
      { HasteKind.Prefab,    "Prefab Icon" },
      { HasteKind.Scene,     "SceneAsset Icon" },
      { HasteKind.Script,    "cs Script Icon" },
      { HasteKind.Asset,     "DefaultAsset Icon" },
      { HasteKind.Texture,   "Texture2D Icon" },
      { HasteKind.Audio,     "AudioClip Icon" },
      { HasteKind.Animation, "AnimationClip Icon" },
      { HasteKind.Animator,  "AnimatorController Icon" },
      { HasteKind.Material,  "Material Icon" },
      { HasteKind.Model,     "Mesh Icon" },
      { HasteKind.Shader,    "Shader Icon" },
      { HasteKind.Font,      "Font Icon" },
    };

    // The star on a favourited row. Not in the table above because it is not a kind --
    // any row can carry it -- but resolved and tested exactly the same way.
    public const string FavoriteName = "Favorite_colored";

    public static Texture2D Favorite {
      get { return Resolve(FavoriteName); }
    }

    // The footer's settings button.
    public const string SettingsName = "Settings Icon";

    public static Texture2D Settings {
      get { return Resolve(SettingsName); }
    }

    public static IEnumerable<KeyValuePair<HasteKind, string>> Names {
      get { return names; }
    }

    public static string NameFor(HasteKind kind) {
      string name;
      return names.TryGetValue(kind, out name) ? name : null;
    }

    // IconContent keeps its own cache, so there is no second one here -- and caching the
    // Texture2D would go stale when the user switches editor skin.
    public static Texture2D For(HasteKind kind) {
      var name = NameFor(kind);
      return name == null ? null : Resolve(name);
    }

    static Texture2D Resolve(string name) {
      var content = EditorGUIUtility.IconContent(name);
      return content == null ? null : content.image as Texture2D;
    }
  }
}
