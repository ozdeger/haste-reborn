using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace Haste {

  public class HasteHierarchyResult : AbstractHasteResult {

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

    // A GameObject cannot be "opened", but the prefab behind one can.
    public override bool CanOpen {
      get { return SourcePrefab() != null; }
    }

    public override void Open() {
      var prefab = SourcePrefab();
      if (prefab != null) {
        AssetDatabase.OpenAsset(prefab);
        return;
      }
      base.Open();
    }

    UnityEngine.Object SourcePrefab() {
      var go = Object as GameObject;
      if (go == null || !PrefabUtility.IsPartOfPrefabInstance(go)) {
        return null;
      }
      return PrefabUtility.GetCorrespondingObjectFromSource(go);
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
