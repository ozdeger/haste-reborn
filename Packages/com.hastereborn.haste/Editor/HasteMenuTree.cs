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

    // ------------------------------------------------------------ what to leave out
    //
    // Unity's "Assets" menu is a menu-bar menu that doubles as the Project window's
    // context menu, so it holds two different kinds of thing: entries that act on the
    // asset you clicked, and entries that act on the whole project. Only the first kind
    // belongs in a palette whose entire job is doing something to one specific item --
    // and for a .png the second kind was most of the list.
    //
    // The two are told apart by measurement rather than by taste: AN ENTRY THAT IS STILL
    // ENABLED WITH NOTHING SELECTED CANNOT BE ACTING ON THE SELECTION. Measured on a
    // stock 6000.3.17f1, that rule hides Refresh, Reimport All, Import New Asset...,
    // Import Package, Open C# Project, Update UXML Schema, View in Import Activity Window
    // and Seed XR Input Bindings -- and all seven "Mobile Dependency Resolver" entries,
    // which is the point: it catches a package's project-wide tooling without being told
    // that package's name, so it keeps working as a project grows.
    //
    // It has exactly two false positives and one bad case, all listed below.

    // Enabled with an empty selection, but genuinely wanted anyway. Prefix-matched on
    // segment boundaries, so "Assets/Create" covers the whole Create submenu.
    static readonly string[] AlwaysShow = {
      // 81 of the 119 Assets entries are under Create. They are all enabled with nothing
      // selected -- creating does not need a selection -- so the rule would take the
      // whole submenu, and creating something next to the thing you clicked is exactly
      // what a context menu is for.
      "Assets/Create",

      // The two false positives. Both work with an empty selection, and both are useful
      // with one.
      "Assets/Reveal in Finder",
      "Assets/Select Dependencies",
    };

    // Entries the rule does not catch -- they need a selection -- but which are still not
    // what a palette is for. Kept deliberately short: every name here is one the rule
    // cannot reason about, and a long list is a list that goes stale.
    static readonly string[] NeverShow = {
      // Authoring a UPM package out of a folder. Nothing to do with the asset you are
      // looking for, and a menu away from a destructive-feeling operation.
      "Assets/Create UPM Package...",
      "Assets/Export As UPM Package...",
    };

    static bool Matches(string[] rules, string path) {
      if (string.IsNullOrEmpty(path)) {
        return false;
      }

      for (int i = 0; i < rules.Length; i++) {
        var rule = rules[i];
        if (!path.StartsWith(rule, StringComparison.Ordinal)) {
          continue;
        }
        // A rule matches the path itself or a whole subtree of it, never a longer name
        // that merely starts the same way.
        if (path.Length == rule.Length || path[rule.Length] == '/') {
          return true;
        }
      }
      return false;
    }

    // Roots the empty-selection rule is applied to.
    //
    // Assets only, and that is measured rather than cautious. Applying the same rule to
    // GameObject cuts its 24 top-level entries to 3, taking "3D Object", "Light",
    // "Camera", "Make Parent", "Move To View", "Center On Children" and most of the rest
    // with it. Those are not project-wide -- they simply have no [MenuItem] validate
    // function, so the editor reports them enabled at all times and the rule cannot tell
    // them apart from Refresh. Unity's Assets entries overwhelmingly DO declare one,
    // which is the whole reason the rule works there.
    //
    // So: apply it where it was measured to work, not where it was measured to fail.
    // Before adding a root here, run the numbers for it first.
    static readonly string[] RuleRoots = { "Assets" };

    static bool UsesProjectWideRule(string root) {
      return MatchesExactly(RuleRoots, root);
    }

    static bool MatchesExactly(string[] names, string name) {
      for (int i = 0; i < names.Length; i++) {
        if (String.Equals(names[i], name, StringComparison.Ordinal)) {
          return true;
        }
      }
      return false;
    }

    // Leaf paths under this root that are enabled with NOTHING selected.
    //
    // Computed once per domain, by emptying the selection and putting it straight back.
    // That is a real side effect, taken deliberately: there is no way to ask a [MenuItem]
    // validate function "what would you say about an empty selection" without giving it
    // one, since validate functions read Selection directly. It happens inside the same
    // call that the actions pane already uses to select the row, so no repaint falls
    // between the two, and the measured cost is a single ~2 ms pass over the 119 Assets
    // entries.
    static Dictionary<string, HashSet<string>> projectWide;

    static HashSet<string> ProjectWide(string root) {
      if (projectWide == null) {
        projectWide = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
      }

      HashSet<string> paths;
      if (projectWide.TryGetValue(root, out paths)) {
        return paths;
      }

      paths = new HashSet<string>(StringComparer.Ordinal);
      var restore = Selection.objects;
      try {
        Selection.objects = new UnityEngine.Object[0];
        foreach (var path in HasteMenuItemSource.ReadPaths(root)) {
          try {
            if (Menu.GetEnabled(path)) {
              paths.Add(path);
            }
          } catch (Exception) {
            // A validate function that throws tells us nothing, so assume nothing.
          }
        }
      } finally {
        Selection.objects = restore;
      }

      projectWide[root] = paths;
      return paths;
    }

    public static bool IsVisible(HasteMenuNode node, string root) {
      if (node == null) {
        return false;
      }

      if (node.IsSubmenu) {
        // A submenu is worth showing exactly when something inside it is.
        for (int i = 0; i < node.Children.Count; i++) {
          if (IsVisible(node.Children[i], root)) {
            return true;
          }
        }
        return false;
      }

      if (!IsEnabled(node)) {
        return false;
      }
      if (Matches(NeverShow, node.Path)) {
        return false;
      }
      if (Matches(AlwaysShow, node.Path)) {
        return true;
      }
      if (!UsesProjectWideRule(root)) {
        return true;
      }
      return !ProjectWide(root).Contains(node.Path);
    }

    // What the actions pane draws for one level.
    public static List<HasteMenuNode> VisibleChildren(HasteMenuNode node, string root) {
      var visible = new List<HasteMenuNode>();
      if (node == null) {
        return visible;
      }

      for (int i = 0; i < node.Children.Count; i++) {
        if (IsVisible(node.Children[i], root)) {
          visible.Add(node.Children[i]);
        }
      }
      return visible;
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
