using UnityEngine;
using UnityEditor;

namespace Haste {
  public static class HasteFooter {
    public static void Draw(string tip) {
      if (!string.IsNullOrEmpty(tip)) {
        EditorGUILayout.LabelField(tip, HasteStyles.GetStyle("Tip"));
      }

      if (Haste.IsIndexing) {
        EditorGUILayout.LabelField(string.Format("(Indexing {0}...)", Haste.IndexingCount), HasteStyles.GetStyle("Indexing"));
      }
    }
  }
}
