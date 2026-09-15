using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Haste {

  public delegate IEnumerable<HasteItem> HasteSourceFactory();

  // When a source crawls.
  public enum HasteSourceCadence {
    // Crawled continuously in the background: started at domain load and restarted
    // whenever the editor says its data changed. For sources that are cheap to walk and
    // rarely change, where keeping the index warm costs nothing worth measuring.
    Background,

    // Crawled only when something asks for it -- which is the palette opening, and
    // nothing else. Change events mark it stale rather than restarting the crawl.
    //
    // This is for the hierarchy, and the reasoning is that it is the worst possible
    // candidate for a background crawl: it is the most volatile source (any hierarchy
    // change invalidates the whole walk) and the most expensive, because the walk is
    // proportional to the scene rather than to what changed -- measured on 6000.3.17f1,
    // 40 ms for 10k GameObjects and 462 ms for 100k. Restarting that on every
    // EditorApplication.hierarchyChanged meant a crawl could not finish before the next
    // event arrived: 46 crawls started and none completed over 8 seconds of ordinary
    // hierarchy edits, with Haste holding 98% of the main thread and the palette closed.
    OnDemand,
  }

  // Manager for various source watchers.
  public class HasteWatcherManager : IEnumerable<KeyValuePair<string, IHasteWatcher>> {

    public bool IsIndexing {
      get { return watchers.Any(w => w.Value.IsIndexing); }
    }

    public int IndexingCount {
      get { return watchers.Sum(w => w.Value.IndexingCount); }
    }

    public int IndexedCount {
      get { return watchers.Sum(w => w.Value.IndexedCount); }
    }

    public ICollection<string> Keys {
      get { return watchers.Keys; }
    }

    // Per manager, not static. There is exactly one of these in a live editor, so the
    // statics these used to be never mattered in production -- but they made two managers
    // silently share one registry, which is indistinguishable from a leak the moment a
    // test builds its own.
    readonly IDictionary<string, IHasteWatcher> watchers =
      new Dictionary<string, IHasteWatcher>();

    readonly IDictionary<string, HasteSourceCadence> cadences =
      new Dictionary<string, HasteSourceCadence>();

    // On-demand sources whose data has changed since their last crawl. Registered stale,
    // because a source that has never crawled is exactly as out of date as one whose
    // scene has been edited since.
    readonly HashSet<string> stale = new HashSet<string>();

    public HasteSourceCadence CadenceOf(string name) {
      HasteSourceCadence cadence;
      return cadences.TryGetValue(name, out cadence) ? cadence : HasteSourceCadence.Background;
    }

    public bool IsStale(string name) {
      return stale.Contains(name);
    }

    // Subscribes the watcher to the index, and starts it crawling if its cadence says so.
    // An on-demand source is wired up here exactly like a background one -- it just does
    // not walk anything until Refresh asks it to.
    void StartSource(string name, IHasteWatcher watcher) {
      watcher.Created += AddToIndex;
      watcher.Deleted += RemoveFromIndex;

      if (CadenceOf(name) == HasteSourceCadence.OnDemand) {
        stale.Add(name);
        return;
      }

      watcher.Start();
    }

    void StopSource(IHasteWatcher watcher, bool purge = false) {
      if (purge) {
        watcher.Purge();
      } else {
        watcher.Stop();
      }

      watcher.Created -= AddToIndex;
      watcher.Deleted -= RemoveFromIndex;
    }

    public void ToggleSource(string name, bool enabled) {
      IHasteWatcher watcher;
      if (watchers.TryGetValue(name, out watcher)) {

        // State changed
        if (enabled != watcher.Enabled) {
          watcher.Enabled = enabled;

          if (enabled) {
            StartSource(name, watcher);
          } else {
            StopSource(watcher, true);
            stale.Remove(name);
          }
        }
      }
    }

    public void AddSource(string name, bool enabled, HasteSourceFactory factory,
                          HasteSourceCadence cadence = HasteSourceCadence.Background) {
      if (!watchers.ContainsKey(name)) {
        IHasteWatcher watcher = new HasteWatcher(factory);
        watcher.Enabled = enabled;

        cadences[name] = cadence;

        if (watcher.Enabled) {
          StartSource(name, watcher);
        }

        watchers.Add(name, watcher);
      }
    }

    public void RemoveSource(string name) {
      IHasteWatcher watcher;
      if (watchers.TryGetValue(name, out watcher)) {
        StopSource(watcher);

        watchers.Remove(name);
        cadences.Remove(name);
        stale.Remove(name);
      }
    }

    // "This source's data changed." Whether that costs anything right now is this
    // method's decision and not the caller's: the editor events that call it fire far
    // too often, and in bursts, to be a crawl trigger on their own.
    public void RestartSource(string name) {
      IHasteWatcher watcher;
      if (watchers.TryGetValue(name, out watcher)) {
        if (!watcher.Enabled) {
          return;
        }

        if (CadenceOf(name) == HasteSourceCadence.OnDemand) {
          stale.Add(name);
          return;
        }

        watcher.Restart();
      }
    }

    // Brings every on-demand source up to date. Called when the palette opens, which is
    // the only moment the index is read.
    //
    // An in-flight crawl is left alone rather than restarted -- it is already walking the
    // current scene, and restarting is what the storm was made of. A crawl that outlives
    // the palette is also left to finish: it is bounded, it is a few frames, and
    // abandoning it throws away the whole walk, because HasteWatcher only commits its
    // collection at the end.
    public void Refresh() {
      foreach (var name in stale.ToArray()) {
        IHasteWatcher watcher;
        if (!watchers.TryGetValue(name, out watcher) || !watcher.Enabled) {
          continue;
        }

        if (watcher.IsIndexing) {
          continue;
        }

        stale.Remove(name);
        watcher.Restart();
      }
    }

    public void Stop() {
      foreach (IHasteWatcher watcher in watchers.Values) {
        watcher.Stop();
      }

      stale.Clear();
    }

    public void Rebuild() {
      foreach (var entry in watchers) {
        if (!entry.Value.Enabled) {
          continue;
        }

        // An on-demand source is emptied and marked stale rather than walked now. The
        // callers -- the Rebuild Index button, an ignore-rule edit, an upgrade -- are
        // asking for the index to be correct next time it is read, not for a crawl to
        // start behind a palette nobody has opened.
        if (CadenceOf(entry.Key) == HasteSourceCadence.OnDemand) {
          entry.Value.Stop();
          stale.Add(entry.Key);
          continue;
        }

        entry.Value.Rebuild();
      }
    }

    void AddToIndex(HasteItem item) {
      Haste.Index.Add(item);
    }

    void RemoveFromIndex(HasteItem item) {
      Haste.Index.Remove(item);
    }

    public IEnumerator<KeyValuePair<string, IHasteWatcher>> GetEnumerator() {
      foreach (var watcher in watchers) {
        yield return watcher;
      }
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }
  }
}
