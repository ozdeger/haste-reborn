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

    // Set when this row opens a submenu rather than doing anything. Run is null on these
    // -- RunAction checks this first, because a submenu is not something that can be run.
    public HasteMenuNode Submenu;
  }

  // What the actions pane offers for a given result.
  //
  // For project assets and scene objects this is the editor's OWN context menu, read live
  // -- see ForMenu and HasteMenuTree. The hand-written list below is what is left: menu
  // items and window layouts are not objects, nothing right-clicks them, and they have no
  // context menu to show.
  public static class HasteItemActions {

    // One level of an item's real context menu, as pane rows.
    public static List<HasteItemAction> ForMenu(HasteMenuNode node, IHasteResult result) {
      var actions = new List<HasteItemAction>();
      if (node == null || result == null) {
        return actions;
      }

      foreach (var child in HasteMenuTree.EnabledChildren(node)) {
        if (child.IsSubmenu) {
          actions.Add(new HasteItemAction {
            Label = child.Label,
            Keys = "\u203a",
            ClosesWindow = false,
            Submenu = child,
          });
          continue;
        }

        var path = child.Path;
        actions.Add(new HasteItemAction {
          Label = child.Label,

          // The design draws destructive actions red. Matching on the label is crude, but
          // a menu item carries no "this deletes things" flag and the alternative is not
          // marking them at all.
          Destructive = child.Label == "Delete",
          Keys = "",
          Run = () => {
            // The selection has to be set again here, not just when the pane opened: the
            // palette restores the previous selection as it closes, and this runs after
            // that. A menu item acts on whatever is selected when it runs.
            var obj = result.Object;
            if (obj != null) {
              Selection.objects = new UnityEngine.Object[] { obj };
            }
            EditorApplication.ExecuteMenuItem(path);
          },
        });
      }

      return actions;
    }

    // The short form, for the footer hint where there is no room for the long one.
    public static string RevealLabelFor(HasteItem item) {
      switch (item.source) {
        case HasteProjectSource.NAME:
        case HasteHierarchySource.NAME: return "Reveal";
        case HasteLayoutSource.NAME:    return "Switch";
        default:                        return "Run";
      }
    }

    // What Enter does to this item, said plainly.
    static string RevealLabel(HasteItem item) {
      switch (item.source) {
        case HasteProjectSource.NAME:   return "Reveal in Project window";
        case HasteHierarchySource.NAME: return "Reveal in Hierarchy";
        case HasteLayoutSource.NAME:    return "Switch to layout";
        default:                        return "Run";
      }
    }

    public static List<HasteItemAction> For(IHasteResult result) {
      var actions = new List<HasteItemAction>();
      if (result == null) {
        return actions;
      }

      var item = result.Item;
      var isAsset = item.source == HasteProjectSource.NAME;

      // First entry is whatever Enter does, named for what that actually is. It used to be
      // called "Open" for everything, which was wrong twice over: Enter reveals rather
      // than opens, and for a menu item it runs.
      actions.Add(new HasteItemAction {
        Label = RevealLabel(item),
        Keys = "↵",
        Run = result.Action,
      });

      if (result.CanOpen) {
        actions.Add(new HasteItemAction {
          Label = "Open",
          Keys = "⇧↵",
          Run = result.Open,
        });
      }

      if (isAsset) {
        // Runtime platform check, never a compile symbol: a Windows-built editor assembly
        // bakes in the compiling editor's symbol.
        actions.Add(new HasteItemAction {
          Label = Application.platform == RuntimePlatform.OSXEditor
            ? "Show in Finder" : "Show in Explorer",
          Keys = "",
          Run = () => EditorUtility.RevealInFinder(item.path),
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
