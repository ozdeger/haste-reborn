using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Haste {

  public class HasteIndex {

    // Every indexed item. This is the source of truth for membership and Count; the
    // character buckets below are a lookup accelerator over it, nothing more.
    readonly HashSet<HasteItem> items = new HashSet<HasteItem>();

    // Items bucketed by the DISTINCT CHARACTERS OF THEIR PATH -- not, as it was before
    // the recall fix, by their word-boundary characters.
    //
    // HasteSearch.Filter looks up exactly one bucket, keyed by the query's first
    // character. Keying on boundaries made that bucket a wrong SUBSET of the candidates:
    // an item was never even scored unless the query's first character began a word
    // somewhere in it. That is why "ollider" and "ysics" used to return nothing at all
    // while "Collider" and "Physics" sat in the index -- the single biggest recall
    // limitation the tool had.
    //
    // Keying on path characters makes the bucket a correct SUPERSET instead: a
    // subsequence match requires every query character to appear in the path, so it
    // certainly requires the first one to. Recall is restored and the acceleration is
    // kept. "First character begins a word" survives as a scoring signal in
    // HasteScoring, which is what preserves the acronym feel.
    readonly IDictionary<char, HashSet<HasteItem>> index =
      new Dictionary<char, HashSet<HasteItem>>();

    // The number of unique items in the index
    public int Count {
      get { return items.Count; }
    }

    // The total size of the index including each indexed reference
    public int Size { get; protected set; }

    public bool TryGetValue(char key, out HashSet<HasteItem> bucket) {
      return index.TryGetValue(key, out bucket);
    }

    public void Add(HasteItem item) {
      // Re-adding an already indexed item is a no-op rather than a double count.
      if (!items.Add(item)) {
        return;
      }

      var path = item.pathLower;
      for (int i = 0; i < path.Length; i++) {
        char c = path[i];

        HashSet<HasteItem> bucket;
        if (!index.TryGetValue(c, out bucket)) {
          bucket = new HashSet<HasteItem>();
          index.Add(c, bucket);
        }

        // Repeated characters in one path share a bucket entry, so Size counts
        // references rather than characters.
        if (bucket.Add(item)) {
          Size++;
        }
      }
    }

    public void Remove(HasteItem item) {
      // Removing something that was never indexed used to decrement Count anyway,
      // which let a watcher's spurious Deleted event drive Count negative.
      if (!items.Remove(item)) {
        return;
      }

      var path = item.pathLower;
      for (int i = 0; i < path.Length; i++) {
        char c = path[i];

        HashSet<HasteItem> bucket;
        if (index.TryGetValue(c, out bucket) && bucket.Remove(item)) {
          Size--;
        }
      }
    }

    public void Clear() {
      items.Clear();
      index.Clear();
      Size = 0;
    }
  }
}
