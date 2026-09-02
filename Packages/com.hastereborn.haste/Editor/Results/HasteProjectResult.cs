using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace Haste {

  public class HasteProjectResult : AbstractHasteResult {

    private UnityEngine.Object unityObject;
    public override UnityEngine.Object Object {
      get {
        if (unityObject == null) {
          unityObject = AssetDatabase.LoadMainAssetAtPath(Item.path);
        }
        return unityObject;
      }
    }

    public override bool IsDraggable {
      get { return true; }
    }

    public override string DragLabel {
      get { return Object.name; }
    }

    public HasteProjectResult(HasteItem item, float score, string[] terms) : base(item, score, terms) {}

    public override void Action() {
      EditorApplication.ExecuteMenuItem("Window/Project");
      EditorUtility.FocusProjectWindow();
      Selection.objects = new UnityEngine.Object[]{Object};
      EditorGUIUtility.PingObject(Selection.activeObject);
    }
  }
}
