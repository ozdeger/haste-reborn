using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // One entry in the palette's actions pane.
  public class HasteItemAction {

    public string Label;
    public string Keys;

    // Drawn in red, per the design.
    public bool Destructive;

    // Whether the palette closes and the work runs afterwards.
    //
    // Anything that touches the project or opens a dialog must, for two reasons: the
    // window closes on focus loss, so a modal confirmation would dismiss the palette out
    // from under itself; and acting while the palette is open can corrupt Unity's layout
    // state, which is why Haste.WindowAction exists at all.
    public bool ClosesWindow = true;

    public Action Run;

    // What the flash confirmation says for an action that stays open.
    public string Confirmation;
  }

  // What the actions pane offers for a given result.
  //
  // Deliberately NOT here: Rename. It needs an inline editing affordance in the pane
  // rather than a one-shot action, and half of one is worse than none.
  public static class HasteItemActions {

    public static List<HasteItemAction> For(IHasteResult result) {
      var actions = new List<HasteItemAction>();
      if (result == null) {
        return actions;
      }

      var item = result.Item;
      var isAsset = item.source == HasteProjectSource.NAME;
      var isHierarchy = item.source == HasteHierarchySource.NAME;

      actions.Add(new HasteItemAction {
        Label = "Open",
        Keys = "↵",
        Run = result.Action,
      });

      if (isAsset) {
        actions.Add(new HasteItemAction {
          Label = "Reveal in Project window",
          Keys = "",
          Run = () => {
            var obj = AssetDatabase.LoadMainAssetAtPath(item.path);
            if (obj == null) {
              return;
            }
            EditorApplication.ExecuteMenuItem("Window/Project");
            EditorUtility.FocusProjectWindow();
            Selection.objects = new UnityEngine.Object[] { obj };
            EditorGUIUtility.PingObject(obj);
          },
        });

        // Runtime platform check, never a compile symbol: a Windows-built editor assembly
        // bakes in the compiling editor's symbol.
        actions.Add(new HasteItemAction {
          Label = Application.platform == RuntimePlatform.OSXEditor
            ? "Show in Finder" : "Show in Explorer",
          Keys = "",
          Run = () => EditorUtility.RevealInFinder(item.path),
        });
      }

      if (isHierarchy) {
        actions.Add(new HasteItemAction {
          Label = "Reveal in Hierarchy",
          Keys = "",
          Run = () => {
            var obj = result.Object;
            if (obj == null) {
              return;
            }
            EditorApplication.ExecuteMenuItem("Window/Hierarchy");
            Selection.objects = new UnityEngine.Object[] { obj };
            EditorGUIUtility.PingObject(obj);
          },
        });
      }

      // Clipboard actions stay open: there is nothing to look at afterwards, and having
      // the palette vanish for a copy is worse than useless when you want two of them.
      actions.Add(new HasteItemAction {
        Label = "Copy Path",
        Keys = "",
        ClosesWindow = false,
        Confirmation = "Path copied",
        Run = () => EditorGUIUtility.systemCopyBuffer = item.path,
      });

      if (isAsset) {
        actions.Add(new HasteItemAction {
          Label = "Copy GUID",
          Keys = "",
          ClosesWindow = false,
          Confirmation = "GUID copied",
          Run = () => EditorGUIUtility.systemCopyBuffer = AssetDatabase.AssetPathToGUID(item.path),
        });

        actions.Add(new HasteItemAction {
          Label = "Duplicate",
          Keys = "",
          Run = () => {
            var copy = AssetDatabase.GenerateUniqueAssetPath(item.path);
            if (AssetDatabase.CopyAsset(item.path, copy)) {
              AssetDatabase.Refresh();
              var obj = AssetDatabase.LoadMainAssetAtPath(copy);
              if (obj != null) {
                Selection.objects = new UnityEngine.Object[] { obj };
                EditorGUIUtility.PingObject(obj);
              }
            }
          },
        });

        actions.Add(new HasteItemAction {
          Label = "Delete",
          Keys = "",
          Destructive = true,
          Run = () => {
            if (!EditorUtility.DisplayDialog("Delete asset",
                  "Move \"" + item.path + "\" to the trash?", "Delete", "Cancel")) {
              return;
            }
            // MoveAssetToTrash rather than DeleteAsset: it goes to the OS trash and is
            // recoverable, which is what Unity's own Assets > Delete does.
            AssetDatabase.MoveAssetToTrash(item.path);
          },
        });
      }

      return actions;
    }
  }
}
