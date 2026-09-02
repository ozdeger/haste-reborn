using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Haste {

  public class HasteHierarchySource : IEnumerable<HasteItem> {

    public const string NAME = "Hierarchy";

    // TODO: Put this somewhere better
    public static IDictionary<int, UnityEngine.Object> Scene =
      new Dictionary<int, UnityEngine.Object>();

    IDictionary<int, string> paths = new Dictionary<int, string>();

    // TODO: Use StringBuilder: pass it in and down; String.Concat is slow
    // TODO: Remove recursion
    string GetTransformPath(Transform transform) {
      int id = transform.gameObject.GetInstanceID();
      string path;

      if (!paths.TryGetValue(id, out path)) {
        if (transform.parent == null) {
          path = transform.gameObject.name;
        } else {
          path = GetTransformPath(transform.parent) + "/" + transform.gameObject.name;
        }

        paths.Add(id, path);
      }

      return path;
    }

    public IEnumerator<HasteItem> GetEnumerator() {
      var allFlags = HideFlags.NotEditable |
        HideFlags.DontSave |
        HideFlags.HideAndDontSave |
        HideFlags.HideInInspector |
        HideFlags.HideInHierarchy;

      Scene.Clear();

      foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>()) {
        if (go == null) {
          // Null-check required since we yield, meaning the
          // results of the find could become invalid.
          continue;
        }

        if ((go.hideFlags & allFlags) != 0) {
          continue;
        }

        // Resources.FindObjectsOfTypeAll returns prefab ASSETS as well as scene objects
        // (measured: 1 of the 6 GameObjects in a bare test project), and the Project
        // source already indexes those. Skip them.
        //
        // This replaces a PrefabType.Prefab/ModelPrefab check. GetPrefabAssetType would
        // be the wrong replacement -- it answers "Regular" for an instance as well as an
        // asset. IsPartOfPrefabAsset is true only for the asset, and unlike the old check
        // it also covers prefab variants, which did not exist when that check was written.
        if (PrefabUtility.IsPartOfPrefabAsset(go)) {
          continue;
        }

        var path = GetTransformPath(go.transform);
        var id = go.transform.GetSiblingIndex(); // go.GetInstanceID();
        var item = new HasteItem(path, id, NAME);
        var hash = item.GetHashCode();
        Scene[hash] = go;
        yield return item;
      }
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }
  }
}
