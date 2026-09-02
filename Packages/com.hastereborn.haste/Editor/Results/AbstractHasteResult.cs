using System;
using UnityEditor;
using UnityEngine;

namespace Haste {

  public abstract class AbstractHasteResult : IHasteResult {

    public HasteItem Item { get; private set; }

    public float Score { get; private set; }

    public bool IsVisible { get; set; }

    public virtual bool IsDraggable {
      get { return false; }
    }

    public virtual float Height(bool isHighlighted) {
      return HasteStyles.ItemHeight;
    }

    public virtual UnityEngine.Object Object {
      get { return null; }
    }

    public virtual string DragLabel {
      get { return ""; }
    }

    public virtual bool IsSelected {
      get {
        return HasteWindow.Instance.IsSelected(Object);
      }
    }

    private int[] indices;
    public int[] Indices {
      get {
        if (indices == null) {
          int[] boundaryIndices = HasteStringUtils.GetBoundaryIndices(Item.path);
          indices = HasteStringUtils.GetWeightedSubsequence(Item.pathLower, terms, boundaryIndices);
        }
        return indices;
      }
    }

    // The query, split into the terms that all had to match. Kept rather than the raw
    // string because highlighting has to be computed per term and merged -- passing a
    // whole "popup crimescene" to GetWeightedSubsequence throws, since the space matches
    // nothing and its backtracker pops an empty stack.
    protected readonly string[] terms;

    public string[] Terms {
      get { return terms; }
    }

    protected AbstractHasteResult(HasteItem item, float score, string[] terms) {
      this.terms = terms ?? new string[0];

      Score = score;
      Item = item;
    }

    // -1: a is before than b
    // 0: a is around b
    // 1: a is after than b
    public int CompareTo(IHasteResult b) {
      // Order by score then fallback to length, then lexical ordering
      if (!HasteMathUtils.Approximately(this.Score, b.Score)) {
        return this.Score > b.Score ? -1 : 1;
      }

      // If scores are equal, order by path length
      if (this.Item.path.Length != b.Item.path.Length) {
        return this.Item.path.Length < b.Item.path.Length ? -1 : 1;
      }

      // If lengths are equal, order lexically
      return EditorUtility.NaturalCompare(this.Item.pathLower, b.Item.pathLower);
    }

    public virtual bool Validate() {
      return true;
    }

    public virtual void Draw(bool isHighlighted) {
      using (new HasteVertical()) {
        // Name
        var nameStyle = isHighlighted ?
          HasteStyles.GetStyle("HighlightedName") :
          HasteStyles.GetStyle("Name");
        EditorGUILayout.LabelField(HasteStringUtils.GetFileName(Item.path), nameStyle);

        // Description
        var descriptionStyle = isHighlighted ?
          HasteStyles.GetStyle("HighlightedDescription") :
          HasteStyles.GetStyle("Description");
        var boldStart = isHighlighted ?
          HasteStyles.HighlightedBoldStart :
          HasteStyles.BoldStart;
        var description = HasteStringUtils.BoldLabel(Item.path, Indices, boldStart, HasteStyles.BoldEnd);
        EditorGUILayout.LabelField(description, descriptionStyle);
      }
    }

    public virtual void Select() {
      if (HasteSettings.SelectEnabled) {
        Selection.activeObject = Object;
      }
    }

    public virtual void Action() {
      // NO-OP
    }
  }
}
