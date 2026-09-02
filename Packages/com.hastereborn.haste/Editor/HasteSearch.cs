using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Haste {

  public class HasteSearch {

    const int MIN_SORT_LEN = 1000;

    readonly HasteItem[] emptyMatches = new HasteItem[0];

    HasteIndex index;

    public HasteSearch(HasteIndex index) {
      this.index = index;
    }

    // Perform fast subsequence filtering
    IEnumerator Filter(string[] terms, IPromise<HasteItem[]> promise) {
      if (terms.Length == 0) {
        promise.Resolve(emptyMatches);
        yield break;
      }

      // Every term has to match, so the first-character bucket of ANY term is already a
      // correct superset of the candidates. Take the smallest of them -- for
      // "popup crimescene" that is whichever of 'p' and 'c' occurs in fewer paths -- which
      // makes a second term a filter that costs nothing rather than one that costs more.
      HashSet<HasteItem> bucket = null;
      int queryBits = 0;
      int longestTerm = 0;

      for (var t = 0; t < terms.Length; t++) {
        var term = terms[t];
        queryBits |= HasteStringUtils.LetterBitsetFromString(term);
        if (term.Length > longestTerm) {
          longestTerm = term.Length;
        }

        HashSet<HasteItem> candidate;
        if (!index.TryGetValue(term[0], out candidate)) {
          // This term's first character occurs in no indexed path at all.
          promise.Resolve(emptyMatches);
          yield break;
        }

        if (bucket == null || candidate.Count < bucket.Count) {
          bucket = candidate;
        }
      }

      // We need to copy the hashset in case the indexer adds an item while we iterate
      var bucketArr = new HasteItem[bucket.Count];
      bucket.CopyTo(bucketArr);

      double startTime = EditorApplication.timeSinceStartup;

      var matches = new List<HasteItem>();
      HasteItem m;
      for (var i = 0; i < bucketArr.Length; i++) {
        m = bucketArr[i];

        // Terms are matched independently and may overlap, so the path only has to be at
        // least as long as the longest single term, not as long as all of them together.
        if (m.pathLower.Length < longestTerm) {
          continue;
        }

        var contains = HasteStringUtils.ContainsChars(m.bitset, queryBits);
        if (!contains) {
          continue;
        }

        var subsequence = HasteStringUtils.ContainsAllSubsequences(m.pathLower, terms);
        if (!subsequence) {
          continue;
        }

        matches.Add(m);

        if (EditorApplication.timeSinceStartup - startTime >= Haste.MAX_ITER_TIME) {
          startTime = EditorApplication.timeSinceStartup;
          yield return null;
        }
      }

      promise.Resolve(matches.ToArray());
    }

    IEnumerator Map(HasteItem[] matches, string[] terms, IPromise<IHasteResult[]> promise) {
      double startTime = EditorApplication.timeSinceStartup;

      var results = new List<IHasteResult>(matches.Length);
      HasteItem m;
      for (var i = 0; i < matches.Length; i++) {
        m = matches[i];

        var score = HasteScoring.Score(m, terms);

        // A zero score means the item matched only as characters scattered through word
        // interiors: no boundary character shared with the query, no substring run, and
        // the query begins neither its name nor its path. Widening the index made those
        // reachable (see HasteIndex), and they carry no signal at all -- so drop them
        // here rather than pad the tail of every short query with them.
        if (score > 0.0f) {
          results.Add(m.GetResult(score, terms));
        }

        if (EditorApplication.timeSinceStartup - startTime >= Haste.MAX_ITER_TIME) {
          startTime = EditorApplication.timeSinceStartup;
          yield return null;
        }
      }

      promise.Resolve(results.ToArray());
    }

    void Swap(IHasteResult[] A, int i, int j) {
      var tmp = A[i];
      A[i] = A[j];
      A[j] = tmp;
    }

    int Partition(IHasteResult[] A, int lo, int hi) {
      var pivotIndex = hi;
      var pivotValue = A[pivotIndex];

      // Put the chosen pivot at A[hi]
      Swap(A, pivotIndex, hi);

      // Compare remaining array elements against pivotValue = A[hi]
      var storeIndex = lo;
      for (var i = lo; i <= hi; i++) {
        if (A[i].CompareTo(pivotValue) == -1) {
          Swap(A, i, storeIndex);
          storeIndex++;
        }
      }

      Swap(A, storeIndex, hi); // Move pivot to its final place

      return storeIndex;
    }

    // In-place async quicksort
    IEnumerator Sort(IHasteResult[] A, int lo, int hi) {
      var len = (hi - lo) + 1;

      if (len < MIN_SORT_LEN) {
        Array.Sort(A, lo, len);
        yield break;
      }

      if (lo < hi) {
        var p = Partition(A, lo, hi);
        yield return Haste.Scheduler.Start(Sort(A, lo, p - 1));
        yield return Haste.Scheduler.Start(Sort(A, p + 1, hi));
      }
    }

    public IEnumerator Search(string query, int count, IPromise<IHasteResult[]> searchResult) {
      // Whitespace separates terms that must all match; see HasteStringUtils.SplitQueryTerms.
      string[] terms = HasteStringUtils.SplitQueryTerms(query);

      // Grab a filtered subset from the index
      var filterResult = new Promise<HasteItem[]>();
      // using (new HasteStopwatch("Filter")) {
      yield return Haste.Scheduler.Start(Filter(terms, filterResult)); // Wait on filter
      // }

      if (filterResult.Reason != null) {
        searchResult.Reject(filterResult.Reason);
        yield break;
      } else if (filterResult.Value == null) {
        searchResult.Reject(new ArgumentNullException("filterResult"));
        yield break;
      }

      // Convert items to results with scores
      var mapResult = new Promise<IHasteResult[]>();
      // using (new HasteStopwatch("Map")) {
      yield return Haste.Scheduler.Start(Map(filterResult.Value, terms, mapResult)); // Wait on map
      // }

      if (mapResult.Reason != null) {
        searchResult.Reject(mapResult.Reason);
        yield break;
      } else if (mapResult.Value == null) {
        searchResult.Reject(new ArgumentNullException("mapResult"));
        yield break;
      }

      // Sort the results based on those scores
      var sorted = mapResult.Value;
      // using (new HasteStopwatch("Sort")) {
      yield return Haste.Scheduler.Start(Sort(sorted, 0, sorted.Length - 1)); // Wait on sort
      // }

      // Take desired count
      searchResult.Resolve(sorted.Take(count).ToArray());
    }
  }
}
