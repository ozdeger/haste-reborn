using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Haste {

  // Per-user recency store: what you picked recently, shown when the palette opens with
  // an empty query, and folded into scoring as HasteItem.userScore.
  //
  // This used to be a ScriptableObject written into the plugin's own folder via
  // AssetDatabase.CreateAsset. That cannot survive packaging: once Haste is installed
  // read-only from a git URL into Library/PackageCache, writing there either fails or is
  // silently discarded on the next reinstall. It now lives in the project's UserSettings
  // folder, which is per-user, per-project, and gitignored.
  //
  // Note ScriptableSingleton does NOT auto-save: hideFlags include DontSaveInEditor and
  // CreateAndLoad re-reads from disk, so any mutation not followed by Save is lost on the
  // next domain reload. Hence the dirty flag and the two save hooks below.
  [FilePath("UserSettings/HasteRecency.asset", FilePathAttribute.Location.ProjectFolder)]
  public class HasteRecommendations : ScriptableSingleton<HasteRecommendations> {

    // Bumped whenever the persisted shape changes so a stale file can be discarded
    // instead of deserialized into nonsense.
    const int SCHEMA_VERSION = 2;

    const float THRESHOLD = 0.1f;
    const float DECAY = 0.9f;

    [SerializeField]
    int schemaVersion;

    [SerializeField]
    List<HasteItem> recent = new List<HasteItem>();

    bool dirty;

    void OnEnable() {
      if (schemaVersion != SCHEMA_VERSION) {
        // Pre-2.0 entries identified items by an unstable hash of (path, sibling index),
        // which is not reversible into anything we can look up, so there is nothing to
        // migrate -- discard and start clean.
        recent.Clear();
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

    public IHasteResult[] Get() {
      return recent.OrderByDescending(item => item.userScore)
        .Select(item => item.GetResult(item.userScore, new string[0]))
        .Where(result => {
          if (result.Item.source == HasteHierarchySource.NAME ||
              result.Item.source == HasteProjectSource.NAME) {
            return result.Object != null;
          } else {
            return true;
          }
        })
        .ToArray();
    }

    public void Add(HasteItem newItem) {
      var index = recent.IndexOf(newItem);
      if (index != -1 && newItem.userScore == 1.0f) {
        return; // Do nothing if we just selected this item
      }

      // Decay recent
      var dead = new List<HasteItem>();
      foreach (var item in recent) {
        item.userScore *= DECAY;

        if (item.userScore < THRESHOLD) {
          dead.Add(item);
        }
      }

      // Remove dead recent
      recent.RemoveAll((item) => dead.Contains(item));

      if (index != -1) {
        recent[index] = newItem; // Replace original instance
      } else {
        recent.Add(newItem); // Add new item
      }

      // Set item score
      newItem.userScore = 1.0f;

      dirty = true;
    }

    // Exposed for the preferences page and for tests.
    public int Count {
      get { return recent.Count; }
    }

    public void Clear() {
      if (recent.Count == 0) {
        return;
      }
      recent.Clear();
      dirty = true;
      Flush();
    }
  }
}
