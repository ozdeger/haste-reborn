using System;

namespace Haste {

  public static class HasteScoring {

    // How much of the acronym score an item keeps when the query's first character does
    // not begin a word anywhere in it.
    //
    // This constant is the recall fix's other half. HasteIndex used to bucket by
    // boundary characters, which made "first character begins a word" a hard FILTER:
    // items failing it were never scored, so "ollider" returned nothing. The index now
    // buckets by path characters and those items are scored, so the same signal has to
    // survive here as a WEIGHT instead -- otherwise interior noise ranks alongside the
    // acronym matches that are the reason to use Haste.
    public const float INTERIOR_START_DAMPING = 0.5f;

    public static float Score(HasteItem item, string queryLower, int queryLen) {
      var userScore = 1.0f + (item.userScore / 10.0f);

      var boundaryMatchCount = HasteStringUtils.LongestCommonSubsequenceLength(queryLower, item.boundariesLower);
      var boundaryQueryRatio = boundaryMatchCount / (float)queryLen;
      var boundaryLen = item.boundariesLower.Length;
      var boundaryUtilization = boundaryLen > 0 ? boundaryMatchCount / (float)boundaryLen : 0.0f;

      float score = 40.0f * boundaryQueryRatio + 40.0f * boundaryUtilization;

      // Damp the acronym component ONLY, never the ladder below it. A literal substring
      // match -- "ollider" inside "Mesh Collider" -- is a strong, deliberate signal, and
      // halving it would let a weak boundary-first match elsewhere ("Old/Slider", where
      // 'o' happens to begin a word) outrank the thing the user actually typed.
      if (item.boundariesLower.IndexOf(queryLower[0]) == -1) {
        score *= INTERIOR_START_DAMPING;
      }

      // Exactly one of the following bonuses applies, strongest first.
      //
      // Every comparison here is Ordinal on purpose. These are paths, not prose, and
      // culture-sensitive comparison genuinely diverges on a tr-TR machine, where
      // "I".ToLower() is the dotless 'i'.

      // Favor exact name matches
      if (item.nameLower == queryLower) {
        score += 60.0f;
        return score * userScore;
      }

      // Favor exact path matches
      if (item.pathLower == queryLower) {
        score += 50.0f;
        return score * userScore;
      }

      // Favor prefix name matches
      if (queryLen >= 3 && item.nameLower.IndexOf(queryLower, StringComparison.Ordinal) == 0) {
        score += 40.0f;
        return score * userScore;
      }

      // Favor prefix path matches
      if (queryLen >= 3 && item.pathLower.IndexOf(queryLower, StringComparison.Ordinal) == 0) {
        score += 30.0f;
        return score * userScore;
      }

      // Favor substring name matches. A contiguous run is a much stronger signal than
      // the single shared character the rung below it tests, and it is what makes the
      // now-reachable interior matches rank usefully instead of tying on zero.
      if (queryLen >= 3 && item.nameLower.IndexOf(queryLower, StringComparison.Ordinal) > 0) {
        score += 25.0f;
        return score * userScore;
      }

      // Favor first char name matches.
      //
      // The length guard is not paranoia: GetFileNameWithoutExtension returns "" for a
      // path that is nothing but an extension, so a GameObject named ".x" indexes with an
      // empty name and this line threw IndexOutOfRangeException. Widening the index made
      // that reachable from more queries than before.
      if (item.nameLower.Length > 0 && item.nameLower[0] == queryLower[0]) {
        score += 20.0f;
        return score * userScore;
      }

      // Favor substring path matches
      if (queryLen >= 3 && item.pathLower.IndexOf(queryLower, StringComparison.Ordinal) > 0) {
        score += 15.0f;
        return score * userScore;
      }

      // Favor first char path matches
      if (item.pathLower.Length > 0 && item.pathLower[0] == queryLower[0]) {
        score += 10.0f;
        return score * userScore;
      }

      return score * userScore;
    }
  }
}
