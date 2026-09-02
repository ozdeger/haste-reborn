using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Haste {

  public static class HasteActions {

    public delegate void MenuItemFallbackDelegate();

    // Implementations for the entries in HasteMenuItemSource.CustomMenuItems -- actions
    // Haste offers that the editor has no menu item for. HasteMenuItemResult looks a path
    // up here first and falls through to EditorApplication.ExecuteMenuItem.
    //
    // This table used to carry 33 more entries: hand-written stand-ins for built-in menu
    // items, from the days when ExecuteMenuItem could not reach them. They are gone.
    // Unity 6 backs ExecuteMenuItem with the same native menu tree the source now
    // enumerates, and 23 of the 33 were keyed on Unity 5 paths that no longer exist, so
    // they could never fire; the other 10 shadowed working menu items with worse
    // behaviour -- "File/New Scene" ran the obsolete EditorApplication.NewScene() instead
    // of opening Unity 6's scene-template dialog, and the clipboard entries posted a
    // command event to EditorWindow.focusedWindow, which is null often enough to matter.
    public static IDictionary<string, MenuItemFallbackDelegate> MenuItemFallbacks = new Dictionary<string, MenuItemFallbackDelegate>() {

      #region CUSTOM
      { "Assets/Instantiate Prefab", () => {
        if (Selection.objects.Length == 0) {
          return;
        }

        var instantiatedPrefabs = new List<UnityEngine.Object>(Selection.objects.Length);

        using (new HasteUndoStack("Instantiate Prefabs")) {
          foreach (var selectedObject in Selection.objects) {
            // Was a PrefabType.Prefab/ModelPrefab check. IsPartOfPrefabAsset is the exact
            // replacement -- GetPrefabAssetType answers "Regular" for instances too --
            // and it additionally covers prefab variants, which the old check predated.
            if (PrefabUtility.IsPartOfPrefabAsset(selectedObject)) {
              var instantiatedPrefab = PrefabUtility.InstantiatePrefab(selectedObject);
              instantiatedPrefabs.Add(instantiatedPrefab);
              Undo.RegisterCreatedObjectUndo(instantiatedPrefab, "Instantiate Prefab");
            }
          }
        }

        Selection.objects = instantiatedPrefabs.ToArray();
      } },

      { "GameObject/Lock", () => {
        if (Selection.gameObjects.Length == 0) {
          return;
        }

        foreach (var selectedObject in Selection.gameObjects) {
          selectedObject.hideFlags |= HideFlags.NotEditable;
          EditorUtility.SetDirty(selectedObject);
        }
      } },

      { "GameObject/Unlock", () => {
        if (Selection.gameObjects.Length == 0) {
          return;
        }

        foreach (var selectedObject in Selection.gameObjects) {
          selectedObject.hideFlags &= ~HideFlags.NotEditable;
          EditorUtility.SetDirty(selectedObject);
        }
      } },

      { "GameObject/Activate", () => {
        if (Selection.gameObjects.Length == 0) {
          return;
        }

        Undo.RecordObjects(Selection.gameObjects, "Activate GameObjects");
        foreach (var selectedObject in Selection.gameObjects) {
          EditorUtility.SetObjectEnabled(selectedObject, true);
        }
      } },

      { "GameObject/Deactivate", () => {
        if (Selection.gameObjects.Length == 0) {
          return;
        }

        Undo.RecordObjects(Selection.gameObjects, "Deactivate GameObjects");
        foreach (var selectedObject in Selection.gameObjects) {
          EditorUtility.SetObjectEnabled(selectedObject, false);
        }
      } },

      { "GameObject/Reset Transform", () => {
        if (Selection.transforms.Length == 0) {
          return;
        }

        Undo.RecordObjects(Selection.transforms, "Reset Transforms");
        foreach (var selectedTransform in Selection.transforms) {
          selectedTransform.localPosition = Vector3.zero;
          selectedTransform.localScale = Vector3.one;
          selectedTransform.localRotation = Quaternion.identity;
        }
      } },

      { "GameObject/Select Parent", () => {
        if (Selection.transforms.Length == 0) {
          return;
        }

        var transforms = new List<Transform>(Selection.transforms.Length);
        foreach (var selectedTransform in Selection.transforms) {
          if (selectedTransform.parent != null) {
            transforms.Add(selectedTransform.parent);
          }
        }

        Selection.objects = transforms.ToArray();
      } },

      { "GameObject/Select Children", () => {
        if (Selection.transforms.Length == 0) {
          return;
        }

        IList<GameObject> children = new List<GameObject>();
        foreach (var selectedTransform in Selection.transforms) {
          if (selectedTransform != null && selectedTransform.childCount > 0) {
            foreach (Transform transform in selectedTransform) {
              children.Add(transform.gameObject);
            }
          }
        }

        Selection.objects = children.ToArray();
      } },

      // Prefab
      { "GameObject/Select Prefab", () => {
        if (Selection.gameObjects.Length == 0) {
          return;
        }

        var objects = new List<UnityEngine.Object>(Selection.objects.Length);
        foreach (var selectedObject in Selection.gameObjects) {
          // GetPrefabParent's replacement. Generic, so this stays a GameObject.
          var parentObject = PrefabUtility.GetCorrespondingObjectFromSource(selectedObject);
          if (parentObject != null) {
            objects.Add(parentObject);
          }
        }

        Selection.objects = objects.ToArray();
      } },

      { "GameObject/Revert to Prefab", () => {
        if (Selection.gameObjects.Length == 0) {
          return;
        }

        using (new HasteUndoStack("Revert to Prefabs")) {
          foreach (var selectedObject in Selection.gameObjects) {
            Undo.RegisterFullObjectHierarchyUndo(selectedObject, "Revert to Prefab");
            // AutomatedAction, not UserAction: undo is already registered on the line
            // above, exactly as it was when this called the obsolete overload, and
            // UserAction would record a second entry and can raise its own dialogs.
            PrefabUtility.RevertPrefabInstance(selectedObject, InteractionMode.AutomatedAction);
          }
        }
      } }
      // "GameObject/Reconnect to Prefab" used to live here and is deliberately gone.
      //
      // It called PrefabUtility.ReconnectToLastPrefab, whose own obsolete message is
      // "This method does nothing." Unity 2018.3 rebuilt the prefab system and removed
      // disconnected instances entirely -- an instance cannot be disconnected, so there is
      // nothing to reconnect, and PrefabInstanceStatus.Disconnected survives only as a
      // legacy enum value the editor never returns. Offering the action would put a row in
      // the palette that silently does nothing when you press Enter.
      #endregion
    };
  }
}