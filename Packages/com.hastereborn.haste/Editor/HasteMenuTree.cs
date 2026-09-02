using System;
using System.Collections.Generic;
using UnityEditor;

namespace Haste {

  // One level of a live editor menu.
  public class HasteMenuNode {

    // The last path segment -- what the menu draws.
    public string Label;

    // The full menu path, for ExecuteMenuItem and Menu.GetEnabled. Null on the synthetic
    // root and on any node that exists only to hold children.
    public string Path;

    public readonly List<HasteMenuNode> Children = new List<HasteMenuNode>();

    public bool IsSubmenu {
      get { return Children.Count > 0; }
    }
  }

  // Turns the editor's flat menu paths into the tree the actions pane walks.
  //
  // The actions pane used to offer a hand-written list -- Copy Path, Duplicate, Delete --
  // which was a guess at what the Project window's context menu contains. It does not have
  // to be a guess: in Unity the "Assets" menu IS the Project window's context menu, and
  // "GameObject" is the Hierarchy's, so the real thing can be read and shown, including
  // whatever the project's own packages have added to it.
  //
  // Build is pure -- it takes the paths rather than reading them -- because the tree
  // shape, the ordering and the submenu detection are the parts worth testing, and none of
  // that needs a live editor.
  public static class HasteMenuTree {

    // The context menu root for an item, or null for an item that has no context menu.
    // Menu items and window layouts are not objects and nothing right-clicks them.
    public static string RootFor(HasteItem item) {
      if (item == null) {
        return null;
      }

      switch (item.source) {
        case HasteProjectSource.NAME:   return "Assets";
        case HasteHierarchySource.NAME: return "GameObject";
      }
      return null;
    }

    // Menu order is preserved rather than sorted: the editor returns items in the order it
    // draws them, and a context menu whose entries move around between openings is worse
    // than one in an unfamiliar order.
    public static HasteMenuNode Build(string root, IEnumerable<string> paths) {
      var tree = new HasteMenuNode { Label = root };
      if (paths == null) {
        return tree;
      }

      var index = new Dictionary<string, HasteMenuNode>(StringComparer.Ordinal);
      var prefix = root + "/";

      foreach (var path in paths) {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.Ordinal)) {
          continue;
        }

        var segments = path.Substring(prefix.Length).Split('/');
        var parent = tree;
        var walked = root;

        for (int i = 0; i < segments.Length; i++) {
          if (segments[i].Length == 0) {
            break;
          }

          walked = walked + "/" + segments[i];

          HasteMenuNode node;
          if (!index.TryGetValue(walked, out node)) {
            node = new HasteMenuNode { Label = segments[i] };
            index.Add(walked, node);
            parent.Children.Add(node);
          }

          // Only the full path is executable. An intermediate segment names a submenu, and
          // "Assets/Create" is not something ExecuteMenuItem can run -- which is why Path
          // is set on the last segment only, and why IsSubmenu is asked before Path is
          // used anywhere.
          if (i == segments.Length - 1) {
            node.Path = walked;
          }

          parent = node;
        }
      }

      return tree;
    }

    public static HasteMenuNode BuildLive(string root) {
      return Build(root, HasteMenuItemSource.ReadPaths(root));
    }

    // Whether the editor would draw this entry as available right now.
    //
    // Menu.GetEnabled is public and answers for a leaf only -- it runs the [MenuItem]
    // validate function, and a submenu has none -- so a submenu counts as enabled when
    // anything inside it is. Measured at 0.09 ms for the 33 top-level Assets entries, so
    // there is no reason to cache it and every reason not to: it depends on the selection,
    // which is exactly what changes between one opening and the next.
    public static bool IsEnabled(HasteMenuNode node) {
      if (node == null) {
        return false;
      }

      if (node.IsSubmenu) {
        for (int i = 0; i < node.Children.Count; i++) {
          if (IsEnabled(node.Children[i])) {
            return true;
          }
        }
        return false;
      }

      if (string.IsNullOrEmpty(node.Path)) {
        return false;
      }

      try {
        return Menu.GetEnabled(node.Path);
      } catch (Exception) {
        // A validate function that throws is the package author's bug, not a reason to
        // lose the rest of the menu.
        return false;
      }
    }

    public static List<HasteMenuNode> EnabledChildren(HasteMenuNode node) {
      var enabled = new List<HasteMenuNode>();
      if (node == null) {
        return enabled;
      }

      for (int i = 0; i < node.Children.Count; i++) {
        if (IsEnabled(node.Children[i])) {
          enabled.Add(node.Children[i]);
        }
      }
      return enabled;
    }
  }
}
