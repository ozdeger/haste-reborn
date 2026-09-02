using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Haste {

  [Serializable]
  public class HasteItem {

    public string path;
    public string pathLower;

    public string name;
    public string nameLower;

    public int id;
    public string source;

    public string boundariesLower;
    public int bitset;

    // There is deliberately no extension field. One existed, assigned from GetExtension in
    // this constructor, and nothing ever read it -- it cost a scan and a string allocation
    // on every indexed item, and it left the impression that searching ".cs" worked by
    // matching an extension. It does not: GetBoundaries emits every "." as a boundary
    // character, so ".cs" matches through ordinary subsequence matching on the path like
    // any other query.

    public float userScore;

    public HasteItem(string path, int id, string source) {
      this.path = path;
      this.pathLower = path.ToLowerInvariant();

      this.name = HasteStringUtils.GetFileNameWithoutExtension(path);
      this.nameLower = name.ToLowerInvariant();

      this.id = id;
      this.source = source;

      this.boundariesLower = HasteStringUtils.GetBoundaries(path);
      this.bitset = HasteStringUtils.LetterBitsetFromString(pathLower);

      this.userScore = 0.0f;
    }

    public IHasteResult GetResult(float score, string queryLower) {
      switch (source) {
        case HasteHierarchySource.NAME:
          return new HasteHierarchyResult(this, score, queryLower);
        case HasteProjectSource.NAME:
          return new HasteProjectResult(this, score, queryLower);
        case HasteMenuItemSource.NAME:
          return new HasteMenuItemResult(this, score, queryLower);
        case HasteLayoutSource.NAME:
          return new HasteMenuItemResult(this, score, queryLower);
        default:
          return new HasteResult(this, score, queryLower);
      }
    }

    public bool Equals(HasteItem other) {
      if (other == null) {
        return false;
      }

      // Reference
      if (other == this) {
        return true;
      }

      return GetHashCode() == other.GetHashCode();
    }

    public override bool Equals(object obj) {
      return Equals(obj as HasteItem);
    }

    public override int GetHashCode() {
      int hash = (int)17;
      hash = hash * 23 ^ id.GetHashCode();
      hash = hash * 23 ^ path.GetHashCode();
      return hash;
    }
  }
}
