using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // Items you have starred, and the multiplier they get for it.
  //
  // Stored beside the recency list, for the same reasons: UserSettings is per-user AND
  // per-project and is gitignored, which is what favourites want -- they name paths in
  // THIS project, and they are nobody else's business. See HasteRecommendations for the
  // ScriptableSingleton saving rules; they apply here too.
  [FilePath("UserSettings/HasteFavorites.asset", FilePathAttribute.Location.ProjectFolder)]
  public class HasteFavorites : ScriptableSingleton<HasteFavorites> {

    // Flat, and applied on top of everything else -- the per-kind weight, the per-menu
    // weight, the recency score. Saying "this one, always" should not be a thing the
    // ranking can talk you out of.
    public const float Multiplier = 2.0f;

    const int SCHEMA_VERSION = 1;

    [SerializeField]
    int schemaVersion;

    // "source|path". Deliberately NOT the HasteItem: its GetHashCode folds in `id`, which
    // for a project asset is its position in enumeration order and changes every time the
    // source is re-indexed. A favourite has to outlive a reimport.
    //
    // The source is part of the key because a path alone is not unique -- "Window/Layouts/
    // Tall" is yielded by both the Layout source and the Menu Item source.
    [SerializeField]
    List<string> keys = new List<string>();

    HashSet<string> keyLookup;
    HashSet<string> pathLookup;
    bool dirty;

    void OnEnable() {
      if (schemaVersion != SCHEMA_VERSION) {
        keys.Clear();
        schemaVersion = SCHEMA_VERSION;
        dirty = true;
      }

      EditorApplication.quitting -= Flush;
      EditorApplication.quitting += Flush;
    }

    void OnDisable() {
      Flush();
    }

    void Flush() {
      if (!dirty) {
        return;
      }
      dirty = false;
      Save(true);
    }

    public static string KeyFor(HasteItem item) {
      return item == null ? null : KeyFor(item.source, item.path);
    }

    public static string KeyFor(string source, string path) {
      return string.IsNullOrEmpty(path) ? null : source + "|" + path;
    }

    // Which sources can be favourited at all.
    //
    // An allow-list, so a new source has to opt in rather than inherit this by accident.
    // Hierarchy is the one deliberately left out: its key would be the object's path in
    // the scene, which changes when it is renamed, reparented, or when the scene is
    // closed -- and a favourite that silently stops matching is worse than not offering
    // one. Project paths move too, but only when someone deliberately moves them.
    public static bool CanFavorite(HasteItem item) {
      return item != null && CanFavorite(item.source);
    }

    public static bool CanFavorite(string source) {
      switch (source) {
        case HasteProjectSource.NAME:
        case HasteMenuItemSource.NAME:
        case HasteLayoutSource.NAME:
          return true;
      }
      return false;
    }

    void EnsureIndex() {
      if (keyLookup != null) {
        return;
      }

      keyLookup = new HashSet<string>(StringComparer.Ordinal);
      pathLookup = new HashSet<string>(StringComparer.Ordinal);

      foreach (var key in keys) {
        if (string.IsNullOrEmpty(key)) {
          continue;
        }
        keyLookup.Add(key);
        var bar = key.IndexOf('|');
        pathLookup.Add(bar < 0 ? key : key.Substring(bar + 1));
      }
    }

    public bool Contains(string source, string path) {
      if (keys.Count == 0) {
        return false;
      }
      EnsureIndex();
      return pathLookup.Contains(path) && keyLookup.Contains(KeyFor(source, path));
    }

    public bool Contains(HasteItem item) {
      if (item == null || keys.Count == 0) {
        return false;
      }

      EnsureIndex();

      // Path first, because it allocates nothing. This runs for every candidate of every
      // keystroke, and building the composite key each time would put a string allocation
      // in the middle of the search loop; only an actual path match pays for one.
      if (!pathLookup.Contains(item.path)) {
        return false;
      }
      return keyLookup.Contains(KeyFor(item));
    }

    // Returns what the item is AFTER the call, so a caller can label a button with it.
    public bool Toggle(HasteItem item) {
      return CanFavorite(item) && Toggle(item.source, item.path);
    }

    public bool Toggle(string source, string path) {
      if (!CanFavorite(source)) {
        return false;
      }

      var key = KeyFor(source, path);
      if (string.IsNullOrEmpty(key)) {
        return false;
      }

      EnsureIndex();

      bool nowFavorite;
      if (keyLookup.Contains(key)) {
        keys.RemoveAll(existing => String.Equals(existing, key, StringComparison.Ordinal));
        nowFavorite = false;
      } else {
        keys.Add(key);
        nowFavorite = true;
      }

      keyLookup = null;
      dirty = true;

      // Written through immediately rather than on quit: a favourite is a deliberate,
      // one-at-a-time act, and losing it to a crash or a domain reload would be worse
      // than the cost of the write.
      Flush();
      return nowFavorite;
    }

    public int Count {
      get { return keys.Count; }
    }

    public static string SourceOf(string key) {
      if (string.IsNullOrEmpty(key)) {
        return "";
      }
      var bar = key.IndexOf('|');
      return bar < 0 ? "" : key.Substring(0, bar);
    }

    public static string PathOf(string key) {
      if (string.IsNullOrEmpty(key)) {
        return "";
      }
      var bar = key.IndexOf('|');
      return bar < 0 ? key : key.Substring(bar + 1);
    }

    public void RemoveKey(string key) {
      if (string.IsNullOrEmpty(key)) {
        return;
      }
      if (keys.RemoveAll(existing => String.Equals(existing, key, StringComparison.Ordinal)) > 0) {
        keyLookup = null;
        dirty = true;
        Flush();
      }
    }

    // Exposed for tests, and for a manage-favourites list if one is ever added.
    public string[] ToArray() {
      return keys.ToArray();
    }

    public void SetAll(IEnumerable<string> next) {
      keys.Clear();
      if (next != null) {
        foreach (var key in next) {
          if (!string.IsNullOrEmpty(key)) {
            keys.Add(key);
          }
        }
      }
      keyLookup = null;
      dirty = true;
      Flush();
    }

    public void Clear() {
      if (keys.Count == 0) {
        return;
      }
      SetAll(null);
    }

    // The search loop's entry point.
    public static float For(HasteItem item) {
      return instance.Contains(item) ? Multiplier : 1.0f;
    }

    // --------------------------------------------------------- Project window

    // Favouriting without opening Haste first, alongside Haste > Ignore. The whole
    // selection at once, and written through once rather than once per asset.
    [MenuItem("Assets/Haste/Add to Favorites")]
    static void AddSelectionToFavorites() {
      instance.SetSelection(true);
    }

    [MenuItem("Assets/Haste/Add to Favorites", true)]
    static bool CanAddSelectionToFavorites() {
      return instance.SelectionWouldChange(true);
    }

    [MenuItem("Assets/Haste/Remove from Favorites")]
    static void RemoveSelectionFromFavorites() {
      instance.SetSelection(false);
    }

    [MenuItem("Assets/Haste/Remove from Favorites", true)]
    static bool CanRemoveSelectionFromFavorites() {
      return instance.SelectionWouldChange(false);
    }

    bool SelectionWouldChange(bool favorite) {
      foreach (var path in SelectedAssetPaths()) {
        if (Contains(HasteProjectSource.NAME, path) != favorite) {
          return true;
        }
      }
      return false;
    }

    void SetSelection(bool favorite) {
      // Order is preserved so the preferences list does not reshuffle itself every time
      // something is added.
      var next = new List<string>(keys);
      var seen = new HashSet<string>(keys, StringComparer.Ordinal);
      var changed = false;

      foreach (var path in SelectedAssetPaths()) {
        var key = KeyFor(HasteProjectSource.NAME, path);

        if (favorite) {
          if (seen.Add(key)) {
            next.Add(key);
            changed = true;
          }
        } else if (seen.Remove(key)) {
          next.RemoveAll(existing => String.Equals(existing, key, StringComparison.Ordinal));
          changed = true;
        }
      }

      if (changed) {
        SetAll(next);
      }
    }

    static List<string> SelectedAssetPaths() {
      var paths = new List<string>();
      var selection = Selection.objects;
      if (selection == null) {
        return paths;
      }

      foreach (var obj in selection) {
        if (obj == null || !AssetDatabase.Contains(obj)) {
          continue;
        }
        var path = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(path)) {
          paths.Add(path);
        }
      }
      return paths;
    }
  }
}
