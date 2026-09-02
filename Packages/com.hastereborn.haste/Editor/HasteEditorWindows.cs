using System;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // Bringing one of Unity's own windows to the front.
  //
  // This exists because the obvious way is wrong: Haste shipped
  // ExecuteMenuItem("Window/Project") from its Unity 5 days, and Unity 6 moved those items
  // under "Window/General/". A missing menu path does not throw -- ExecuteMenuItem logs a
  // native error and returns false -- so pressing Enter on any asset printed a stack trace
  // and did not focus anything.
  //
  // The paths are therefore resolved from the live menu rather than written down, which is
  // the same reason HasteMenuItemSource stopped shipping a hardcoded table.
  public static class HasteEditorWindows {

    static string projectMenu;
    static string hierarchyMenu;
    static bool resolved;

    public static string ProjectMenuPath {
      get { Resolve(); return projectMenu; }
    }

    public static string HierarchyMenuPath {
      get { Resolve(); return hierarchyMenu; }
    }

    public static void FocusProject() {
      Execute(ProjectMenuPath);

      // Public, needs no menu path, and focuses the Project browser specifically. Belt and
      // braces: if the menu ever moves again, this still does the useful half.
      EditorUtility.FocusProjectWindow();
    }

    public static void FocusHierarchy() {
      Execute(HierarchyMenuPath);
    }

    static void Execute(string path) {
      // Never call ExecuteMenuItem with a path that is not there -- that is what produced
      // the error report rather than a quiet failure.
      if (!string.IsNullOrEmpty(path)) {
        EditorApplication.ExecuteMenuItem(path);
      }
    }

    static void Resolve() {
      if (resolved) {
        return;
      }
      resolved = true;

      string[] paths;
      try {
        paths = Unsupported.GetSubmenus("Window");
      } catch (Exception) {
        return;
      }

      projectMenu = Find(paths, "Project");
      hierarchyMenu = Find(paths, "Hierarchy");
    }

    // Matches on the LAST path segment, so "Window/General/Hierarchy" is found while
    // "Window/Accessibility/Hierarchy Viewer" is not. The shortest match wins, which keeps
    // the top-level window ahead of anything nested more deeply under a package's menu.
    static string Find(string[] paths, string leaf) {
      if (paths == null) {
        return null;
      }

      string best = null;

      for (int i = 0; i < paths.Length; i++) {
        var path = paths[i];
        if (string.IsNullOrEmpty(path)) {
          continue;
        }

        var slash = path.LastIndexOf('/');
        var last = slash < 0 ? path : path.Substring(slash + 1);

        if (!string.Equals(last, leaf, StringComparison.Ordinal)) {
          continue;
        }

        if (best == null || path.Length < best.Length) {
          best = path;
        }
      }

      return best;
    }
  }
}
