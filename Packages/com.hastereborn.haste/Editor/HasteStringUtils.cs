using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Haste {

  public static class HasteStringUtils {

    static readonly string[] noTerms = new string[0];
    static readonly char[] termSeparators = new []{' ', '\t'};

    // Splits a query into the terms that must ALL match, lowercased.
    //
    // Haste used to treat the whole query as a single subsequence, which meant a space had
    // to occur literally in the path. Paths almost never contain one, so typing a space
    // silently emptied the result list: "popup crimescene" found nothing at all, not even
    // "Popup_CrimeScene_Character_Banner_Sale.png".
    public static string[] SplitQueryTerms(string query) {
      if (string.IsNullOrEmpty(query)) {
        return noTerms;
      }

      var terms = query.ToLowerInvariant().Split(termSeparators, StringSplitOptions.RemoveEmptyEntries);
      return terms.Length == 0 ? noTerms : terms;
    }

    // True when every term appears in `str` as a subsequence. Terms are matched
    // independently, so they may overlap -- "ab" and "bc" both match "abc".
    public static bool ContainsAllSubsequences(string str, string[] terms) {
      var strLen = str.Length;
      for (int i = 0; i < terms.Length; i++) {
        if (!ContainsSubsequence(str, terms[i], strLen, terms[i].Length)) {
          return false;
        }
      }
      return true;
    }

    public static int LongestCommonSubsequenceLength(string first, string second) {
      string longer = first.Length > second.Length ? first : second;
      string shorter = first.Length > second.Length ? second : first;

      int longerLen  = longer.Length;
      int shorterLen = shorter.Length;

      int[] previous = new int[shorterLen + 1];
      int[] current = new int[shorterLen + 1];

      for (int i = 0; i < longerLen; i++) {
        for (int j = 0; j < shorterLen; j++) {
          if (longer[i] == shorter[j]) {
            current[j + 1] = previous[j] + 1;
          } else {
            if (current[j] >= previous[j + 1]) {
              current[j + 1] = current[j];
            } else {
              current[j + 1] = previous[j + 1];
            }
          }
        }

        for (int j = 0; j < shorterLen; j++) {
          previous[j + 1] = current[j + 1];
        }
      }

      return current[shorterLen];
    }

    public static int LetterBitsetFromString(string str) {
      int bits = 0;
      int mask;
      char c;
      for (int i = 0; i < str.Length; i++) {
        c = str[i];
        mask = 1 << (int)c;
        bits |= mask;
      }
      return bits;
    }

    public static bool ContainsChars(int a, int b) {
      return (a & b) == b;
    }

    public static bool ContainsSubsequence(string str, string query, int strLen, int queryLen) {
      if (queryLen > strLen) {
        return false;
      }

      char strChar, queryChar;
      int queryIndex = 0;
      int strIndex = 0;

      while (strIndex < strLen && queryIndex < queryLen) {
        queryChar = query[queryIndex];
        strChar = str[strIndex];

        if (queryChar == strChar) {
          queryIndex++;
          strIndex++;
        } else {
          strIndex++;
        }
      }

      return queryIndex == queryLen;
    }

    public static List<List<int>> GetQueryMatchIndices(string path, string query, int[] boundaryIndices) {
      List<List<int>> queryMatchIndices = new List<List<int>>();

      char c;
      for (int queryIndex = 0; queryIndex < query.Length; queryIndex++) {
        c = query[queryIndex];

        List<int> orderedChars = new List<int>(path.Length);
        List<int> nonBoundaryChars = new List<int>();

        for (int pathIndex = 0; pathIndex < path.Length; pathIndex++) {
          if (c == path[pathIndex]) {
            if (Array.IndexOf(boundaryIndices, pathIndex) != -1) {
              orderedChars.Add(pathIndex);
            } else {
              nonBoundaryChars.Add(pathIndex);
            }
          }
        }

        orderedChars.AddRange(nonBoundaryChars);
        queryMatchIndices.Add(orderedChars);
      }

      return queryMatchIndices;
    }

    public static int[] GetWeightedSubsequence(string path, string query, int[] boundaryIndices) {
      // A list rather than a stack: the rule below needs the last two choices, not just
      // the last one. It is already in order, so the Reverse() this used to end with is
      // gone too.
      List<int> results = new List<int>(query.Length);

      List<List<int>> queryIndices = GetQueryMatchIndices(path, query, boundaryIndices);

      int invalidResult = -1;

      int i = 0;
      while (results.Count < query.Length) {
        List<int> charIndices = queryIndices[i];

        if (invalidResult != -1) {
          queryIndices[i] = charIndices = charIndices.Where(x => x < invalidResult).ToList();
        }

        bool matchedSomething = false;
        int greatestResult = -1;

        // An established run beats a word boundary.
        //
        // GetQueryMatchIndices lists boundary positions FIRST, so by default the loop
        // below takes a boundary character even when the run could simply have continued.
        // That is what highlighted "InfoCollectionOverrideJson" as "InfoCollecti" and then
        // jumped to the capital O of "Override", and what made
        // "Assets/InfoCollections/LiveCollections" highlight six characters of the first
        // segment and the rest of the word in the second.
        //
        // ESTABLISHED, and that qualifier is the whole design. Preferring contiguity
        // unconditionally breaks acronyms, which is the feature Haste is for: "abc"
        // against "Abples/Bananas/Cherribs" would take the "b" sitting next to the "A"
        // and never reach "Bananas". One adjacent character is ambiguous -- it could be
        // the second letter of a word or the start of an acronym hop. Two consecutive
        // already are a literal substring, and abandoning one of those for a boundary
        // further along is never right.
        //
        // So: a run of two or more continues; anything shorter defers to the boundary
        // preference, and acronym matching is untouched.
        //
        // Safe against the backtracker: the filter above has already removed the choice
        // that failed, so a rejected index is not offered again and the search cannot loop.
        var last = results.Count - 1;
        var inRun = results.Count >= 2 && results[last] == results[last - 1] + 1;
        var contiguous = inRun ? results[last] + 1 : -1;

        if (contiguous != -1 && charIndices.Contains(contiguous)) {
          greatestResult = contiguous;
          matchedSomething = true;
        } else {
          for (int j = 0; j < charIndices.Count; j++) {
            if (charIndices[j] > greatestResult) {
              greatestResult = charIndices[j];
              if (results.Count == 0 || greatestResult > results[results.Count - 1]) {
                matchedSomething = true;
                break;
              }
            }
          }
        }

        if (matchedSomething) {
          results.Add(greatestResult);
          i++;
          invalidResult = -1;
        } else {
          results.RemoveAt(results.Count - 1);
          i--;
          invalidResult = greatestResult;
        }
      }

      return results.ToArray();
    }

    // Whether the '.' at `index` separates a file extension.
    //
    // A dot with another dot beside it does not: Unity dialog menu items end in "...",
    // and reading that ellipsis as an extension gave "Component/Add..." the name "Add.."
    // and the extension "..". A lone trailing dot is still a separator with an empty
    // extension, which is the long-standing reading of "test." as name "test".
    static bool IsExtensionSeparator(string path, int index) {
      if (index > 0 && path[index - 1] == '.') {
        return false;
      }
      if (index < path.Length - 1 && path[index + 1] == '.') {
        return false;
      }
      return true;
    }

    // Highlight positions for a multi-term query: the union of each term's own weighted
    // subsequence, sorted, with duplicates removed because BoldLabel splices markup at
    // each index in order and would otherwise wrap a shared character twice.
    public static int[] GetWeightedSubsequence(string path, string[] terms, int[] boundaryIndices) {
      if (terms.Length == 0) {
        return new int[0];
      }

      if (terms.Length == 1) {
        return GetWeightedSubsequence(path, terms[0], boundaryIndices);
      }

      var merged = new HashSet<int>();
      for (int i = 0; i < terms.Length; i++) {
        foreach (var index in GetWeightedSubsequence(path, terms[i], boundaryIndices)) {
          merged.Add(index);
        }
      }

      var indices = new int[merged.Count];
      merged.CopyTo(indices);
      Array.Sort(indices);
      return indices;
    }

    // Highlight positions for ONE PART of a row -- the name, or the directory -- rather
    // than the whole path.
    //
    // Terms that do not occur in `str` are skipped, and that is a correctness requirement
    // rather than a nicety: with a multi-term query a term can match in the directory and
    // not in the name, and GetWeightedSubsequence throws on a term it cannot place --
    // its backtracker pops an empty stack.
    public static int[] GetHighlightIndices(string str, string[] terms) {
      if (string.IsNullOrEmpty(str) || terms == null || terms.Length == 0) {
        return new int[0];
      }

      var lower = str.ToLowerInvariant();

      var present = new List<string>(terms.Length);
      for (int i = 0; i < terms.Length; i++) {
        if (ContainsSubsequence(lower, terms[i], lower.Length, terms[i].Length)) {
          present.Add(terms[i]);
        }
      }

      if (present.Count == 0) {
        return new int[0];
      }

      return GetWeightedSubsequence(lower, present.ToArray(), GetBoundaryIndices(str));
    }

    // The directory part of a path -- what the design shows right-aligned, opposite the
    // name. "" for a bare name with no separator.
    public static string GetDirectory(string path) {
      if (string.IsNullOrEmpty(path)) {
        return "";
      }

      var trimmed = path.TrimEnd('/');
      var sep = trimmed.LastIndexOf('/');
      return sep <= 0 ? "" : trimmed.Substring(0, sep);
    }

    public static string GetFileName(string path) {
      var len = path.Length;
      if (len == 0) {
        return "";
      }

      var sep = path.LastIndexOf('/');
      if (sep == len - 1) {
        path = path.TrimEnd(new []{'/'});
        sep = path.LastIndexOf('/');
      }

      if (sep != -1) {
        return path.Substring(sep + 1);
      } else {
        return path;
      }
    }

    public static string GetExtension(string path) {
      var len = path.Length;
      if (len == 0) {
        return "";
      }

      var sep = path.LastIndexOf('/');

      // Remove trailing slash before getting filename
      if (sep == len - 1) {
        path = path.TrimEnd(new []{'/'});
        sep = path.LastIndexOf('/');
      }

      int ext = -1;
      if (sep != -1) {
        ext = path.IndexOf('.', sep);
      } else {
        ext = path.LastIndexOf('.');
      }

      if (ext != -1 && IsExtensionSeparator(path, ext)) {
        return path.Substring(ext + 1);
      } else {
        return "";
      }
    }

    public static string GetFileNameWithoutExtension(string path) {
      var len = path.Length;
      if (len == 0) {
        return "";
      }

      var sep = path.LastIndexOf('/');

      // Remove trailing slash before getting filename
      if (sep == len - 1) {
        path = path.TrimEnd(new []{'/'});
        sep = path.LastIndexOf('/');
      }

      var ext = path.LastIndexOf('.');
      if (ext != -1 && !IsExtensionSeparator(path, ext)) {
        ext = -1;
      }

      if (sep != -1 && ext != -1) {
        if (ext < sep) {
          sep = sep + 1;
          return path.Substring(sep);
        } else {
          sep = sep + 1;
          return path.Substring(sep, ext - sep);
        }
      } else if (sep != -1) {
        sep = sep + 1;
        return path.Substring(sep);
      } else if (ext != -1) {
        return path.Substring(0, ext);
      } else {
        return path;
      }
    }

    public static int[] GetBoundaryIndices(string str) {
      int len = str.Length;
      List<int> indices = new List<int>();

      if (len == 0) {
        return indices.ToArray();
      }

      char c, _c;
      for (int i = 0; i < len; i++) {
        c = str[i];

        // Is it a word char at the beginning of the string?
        if (i == 0) {
          if (!char.IsPunctuation(c)) {
            indices.Add(i);
          }
        } else {
          _c = str[i - 1];

          // Include extensions
          if (c == '.') {
            indices.Add(i);
            continue;
          }

          // Is it an upper char proceeding a lowercase char or whitespace?
          if (char.IsUpper(c) && !char.IsUpper(_c)) {
            indices.Add(i);
            continue;
          }

          // Is it a post-boundary word char?
          if (char.IsLetterOrDigit(c) && (char.IsPunctuation(_c) || _c == ' ')) {
            indices.Add(i);
            continue;
          }
        }
      }

      return indices.ToArray();
    }

    // It's faster to lowercase each char during iteration rather
    // than ToLowerInvariant at the end.
    public static string GetBoundaries(string str) {
      int len = str.Length;
      if (len == 0) {
        return "";
      }

      // Initializing the string builder with some default capacity helps.
      StringBuilder matches = new StringBuilder(len / 2, len);

      char c, _c;
      for (int i = 0; i < len; i++) {
        c = str[i];

        // Is it a word char at the beginning of the string?
        if (i == 0) {
          if (!char.IsPunctuation(c)) {
            matches.Append(char.ToLowerInvariant(c));
          }
        } else {
          _c = str[i - 1];

          // Include extensions
          if (c == '.') {
            matches.Append(char.ToLowerInvariant(c));
            continue;
          }

          // Is it an upper char proceeding a lowercase char or whitespace?
          if (char.IsUpper(c) && !char.IsUpper(_c)) {
            matches.Append(char.ToLowerInvariant(c));
            continue;
          }

          // Is it a post-boundary word char?
          if (char.IsLetterOrDigit(c) && (char.IsPunctuation(_c) || _c == ' ')) {
            matches.Append(char.ToLowerInvariant(c));
            continue;
          }
        }
      }

      return matches.ToString();
    }

    public static string BoldLabel(string str, int[] indices, string boldStart = "<color=\"white\">", string boldEnd = "</color>") {
      int indicesLen = indices.Length;
      if (indicesLen == 0) {
        return str;
      }

      // Initialize StringBuilder with maximum new length.
      int maxCap = str.Length + ((boldStart.Length + boldEnd.Length) * indicesLen);
      StringBuilder bolded = new StringBuilder(str, maxCap);

      int index;
      int offset = 0;

      for (int i = 0; i < indicesLen; i++) {
        index = indices[i];
        bolded.Insert(index + offset, boldStart);
        offset += boldStart.Length;
        bolded.Insert(index + offset + 1, boldEnd);
        offset += boldEnd.Length;
      }

      return bolded.ToString();
    }

    public static string TrimStart(this String str, string prefix) {
      if (str.StartsWith(prefix)) {
        str = str.Remove(0, prefix.Length);
      }
      return str;
    }

    public static string TrimEnd(this String str, string postfix) {
      if (str.EndsWith(postfix)) {
        str = str.Remove(str.Length - postfix.Length, postfix.Length);
      }
      return str;
    }

    public static string[] Split(this String str, int[] at) {
      List<string> parts = new List<string>(at.Length + 1);

      int offset = 0;
      foreach (var index in at) {
        int offsetIndex = index - offset;
        if (offsetIndex == 0) {
          continue;
        }

        string part = str.Substring(0, offsetIndex);

        if (part != "") {
          parts.Add(part);
        }

        str = str.Substring(offsetIndex);
        offset += offsetIndex;
      }

      if (str != "") {
        parts.Add(str);
      }

      return parts.ToArray();
    }
  }
}
