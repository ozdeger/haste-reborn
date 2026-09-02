using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace Haste {

  public class HasteHierarchyResult : AbstractHasteResult {

    private static Texture gameObjectIcon;

    public static void LoadGameObjectIcon() {
      gameObjectIcon = EditorGUIUtility.ObjectContent(null, typeof(GameObject)).image;
    }

    public static Texture GameObjectIcon {
      get {
        if (gameObjectIcon == null) {
          LoadGameObjectIcon();
        }
        return gameObjectIcon;
      }
    }

    private UnityEngine.Object obj;
    public override UnityEngine.Object Object {
      get {
        if (obj == null) {
          HasteHierarchySource.Scene.TryGetValue(this.Item.GetHashCode(), out obj); // EditorUtility.InstanceIDToObject(Item.id);
        }
        return obj;
      }
    }

    public override bool IsDraggable {
      get { return true; }
    }

    public override string DragLabel {
      get {
        if (Object == null) {
          return "<destroyed>";
        }
        return Object.name;
      }
    }

    public HasteHierarchyResult(HasteItem item, float score, string[] terms) : base(item, score, terms) {}

    // How a hierarchy row is coloured. The names match Unity's own vocabulary for the
    // same three states -- HierarchyProperty.colorCode is {0 Normal, 1 Prefab,
    // 2 BrokenPrefab} -- because this is deliberately the same distinction the editor's
    // Hierarchy window draws.
    public enum PrefabDisplay {
      Normal,
      Prefab,
      BrokenPrefab,
    }

    // Split out from the style lookup so the decision is testable: GUIStyle needs an
    // interactive editor, this does not.
    //
    // Replaces PrefabUtility.GetPrefabType, whose PrefabType enum went obsolete when Unity
    // 2018.3 split "what kind of prefab asset is this" from "what is this instance's
    // relationship to its asset". Measured on 6000.3.17f1, which is why the mapping below
    // is exact rather than inferred:
    //
    //   object            GetPrefabAssetType   GetPrefabInstanceStatus
    //   prefab asset      Regular              NotAPrefab
    //   prefab instance   Regular              Connected
    //   plain GameObject  NotAPrefab           NotAPrefab
    //
    // Note GetPrefabAssetType answers "Regular" for an asset AND an instance, so it is the
    // wrong question to ask here. Instance status is the right one.
    public static PrefabDisplay ClassifyPrefab(GameObject go) {
      if (go == null) {
        return PrefabDisplay.Normal;
      }

      switch (PrefabUtility.GetPrefabInstanceStatus(go)) {
        case PrefabInstanceStatus.MissingAsset:
          // Was PrefabType.MissingPrefabInstance.
          return PrefabDisplay.BrokenPrefab;
        case PrefabInstanceStatus.Connected:
          // Was PrefabType.PrefabInstance / ModelPrefabInstance.
          return PrefabDisplay.Prefab;
        default:
          // NotAPrefab, and the legacy Disconnected the modern prefab system never
          // produces. Disconnected instances fell through to Normal before this change
          // too, so leaving it here keeps the colouring identical.
          return PrefabDisplay.Normal;
      }
    }

    GUIStyle GetLabelStyle(GameObject go, bool isHighlighted) {
      if (go == null) {
        return isHighlighted ? HasteStyles.GetStyle("HighlightedDisabledName") :
          HasteStyles.GetStyle("DisabledName");
      }

      var active = go.activeInHierarchy;

      switch (ClassifyPrefab(go)) {
        case PrefabDisplay.Prefab:
          if (active) {
            return isHighlighted ? HasteStyles.GetStyle("HighlightedPrefabName") :
              HasteStyles.GetStyle("PrefabName");
          } else {
            return isHighlighted ? HasteStyles.GetStyle("HighlightedDisabledPrefabName") :
              HasteStyles.GetStyle("DisabledPrefabName");
          }
        case PrefabDisplay.BrokenPrefab:
          if (active) {
            return isHighlighted ? HasteStyles.GetStyle("HighlightedBrokenPrefabName") :
              HasteStyles.GetStyle("BrokenPrefabName");
          } else {
            return isHighlighted ? HasteStyles.GetStyle("HighlightedDisabledBrokenPrefabName") :
              HasteStyles.GetStyle("DisabledBrokenPrefabName");
          }
        default:
          if (active) {
            return isHighlighted ? HasteStyles.GetStyle("HighlightedName") :
              HasteStyles.GetStyle("Name");
          } else {
            return isHighlighted ? HasteStyles.GetStyle("HighlightedDisabledName") :
              HasteStyles.GetStyle("DisabledName");
          }
      }
    }

    public override void Draw(bool isHighlighted) {
      GameObject go = (GameObject)Object;

      var rect = EditorGUILayout.GetControlRect(GUILayout.Width(32), GUILayout.Height(32));
      rect.y += 5; // center the icon vertically
      GUI.DrawTexture(rect, GameObjectIcon);

      using (new HasteVertical()) {
        var childCount = 0;
        if (go != null && go.transform != null) {
          childCount = go.transform.childCount;
        }

        // Name
        GUIStyle nameStyle = GetLabelStyle(go, isHighlighted || IsSelected);
        string name;
        if (childCount > 0) {
          name = String.Format("{0} ({1})", HasteStringUtils.GetFileName(Item.path), childCount);
        } else if (go == null) {
          name = String.Format("{0} <destroyed>", HasteStringUtils.GetFileName(Item.path), childCount);
        } else {
          name = HasteStringUtils.GetFileName(Item.path);
        }
        EditorGUILayout.LabelField(name, nameStyle);

        // Description
        string boldStart = isHighlighted ? HasteStyles.HighlightedBoldStart : HasteStyles.BoldStart;
        GUIStyle descriptionStyle = isHighlighted ? HasteStyles.GetStyle("HighlightedDescription") : HasteStyles.GetStyle("Description");
        EditorGUILayout.LabelField(HasteStringUtils.BoldLabel(Item.path, Indices, boldStart, HasteStyles.BoldEnd), descriptionStyle);
      }
    }

    public override void Action() {
      EditorApplication.ExecuteMenuItem("Window/Hierarchy");

      // Results outlive the objects they point at -- the scene can change between the
      // search and the Enter. This used to call Object.GetInstanceID() unguarded.
      var target = Object;
      if (target == null) {
        return;
      }

      // Selection.instanceIDs and activeInstanceID are obsolete, and their suggested
      // replacements (entityIds, activeEntityId) do not exist in 6000.0 -- see HANDOFF
      // 3.3. Selection.objects is clean on both editors and PingObject takes the object
      // directly, so nothing has to round-trip through an id at all.
      Selection.objects = new UnityEngine.Object[] { target };
      EditorGUIUtility.PingObject(target);
    }
  }
}
