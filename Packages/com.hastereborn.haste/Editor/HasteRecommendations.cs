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

      // A list written by a version with the position bug can already contain the same
      // item twice, which is what showed up as doubled rows on the launch screen.
      if (Dedupe(recent)) {
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
      if (Record(recent, newItem)) {
        dirty = true;
      }
    }

    // Pure, and static, so the rule can be exercised on a plain list.
    //
    // It used to work out a POSITION before removing the decayed entries and then write
    // to that position afterwards:
    //
    //     var index = recent.IndexOf(newItem);   // here
    //     recent.RemoveAll(dead);                // the list shrinks
    //     recent[index] = newItem;               // and this is now the wrong slot
    //
    // Shrink by a little and `index` addresses a DIFFERENT entry: that one is overwritten
    // and the original copy of newItem survives, so the item appears twice in the recents
    // list and some unrelated recent is silently gone. Shrink by more than that and the
    // index is past the end, which is the ArgumentOutOfRangeException this threw from
    // HasteSpotlightWindow.Act.
    //
    // There is no index now. Any surviving copy is removed and the new one appended --
    // order does not matter, because Get sorts by score.
    public static bool Record(List<HasteItem> recent, HasteItem newItem) {
      if (recent == null || newItem == null) {
        return false;
      }

      // Already the most recent thing picked: nothing to decay and nothing to move.
      if (newItem.userScore == 1.0f && recent.Contains(newItem)) {
        return false;
      }

      for (int i = 0; i < recent.Count; i++) {
        recent[i].userScore *= DECAY;
      }
      recent.RemoveAll(item => item == null || item.userScore < THRESHOLD);

      // Removing every equal copy rather than one also heals a list the old code had
      // already duplicated into.
      recent.RemoveAll(item => item.Equals(newItem));

      newItem.userScore = 1.0f;
      recent.Add(newItem);
      return true;
    }

    // Collapses duplicates left behind by the bug above, keeping the highest score of
    // each. Runs on load, because a list already written to disk does not repair itself:
    // the fix above only heals the item you happen to pick next.
    public static bool Dedupe(List<HasteItem> recent) {
      if (recent == null) {
        return false;
      }

      var best = new Dictionary<HasteItem, HasteItem>();
      var order = new List<HasteItem>();

      foreach (var item in recent) {
        if (item == null) {
          continue;
        }

        HasteItem kept;
        if (!best.TryGetValue(item, out kept)) {
          best.Add(item, item);
          order.Add(item);
        } else if (item.userScore > kept.userScore) {
          best[item] = item;
        }
      }

      if (order.Count == recent.Count) {
        return false;
      }

      recent.Clear();
      foreach (var item in order) {
        recent.Add(best[item]);
      }
      return true;
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
