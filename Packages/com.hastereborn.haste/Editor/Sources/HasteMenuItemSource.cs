using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Haste {

  // Enumerates the editor's menu items.
  //
  // This used to ship two hardcoded lists of menu paths -- one for Unity 4.6, one for
  // Unity 5 -- and choose between them with a version check. On Unity 6 that meant
  // indexing the Unity 5 list, of which 109 of 241 paths (45%, measured on 6000.3.17f1)
  // no longer exist: results that look real and do nothing when you press Enter. At the
  // same time 384 menu items that *do* exist were missing entirely.
  //
  // The editor can simply be asked. `UnityEditor.Menu.GetMenuItems` is internal but
  // present and identical in 6000.0 and 6000.3, and returns 529 clean paths across every
  // root in under 1 ms. `UnityEditor.Unsupported.GetSubmenus` is public and returns the
  // same flattened list, so it is the fallback if the internal one ever goes away.
  //
  // Measured on the live tree, 6000.3.17f1: no path ends in '/', none carries a shortcut
  // suffix, and there are no duplicates -- so the parsing the old attribute-scanning path
  // needed is gone with it.
  public class HasteMenuItemSource : IEnumerable<HasteItem> {

    public const string NAME = "Menu Item";

    // Haste's own menu entry, kept out of Haste's own results. Must stay in step with the
    // [MenuItem] on HasteShortcut.Open; a test pins the two together.
    const string SelfMenuItem = "Window/Haste";

    // `Menu.GetMenuItems("")` returns 0 -- there is no root enumeration and no public root
    // API -- so the roots have to be named. These are the editor's own. Any *other* root,
    // whether from a Unity package or a user's own tools menu, is discovered from
    // [MenuItem] attributes below.
    static readonly string[] BuiltinRoots = {
      "File", "Edit", "Assets", "GameObject", "Component", "Window", "Help",
    };

    // Every root the menu bar has right now: the editor's own, plus whatever [MenuItem]
    // attributes invented. HasteMenuWeights lists these in preferences so a project's own
    // tools menu can be weighted apart from Unity's.
    //
    // Sorted, and the editor's own first, because that is the order the menu bar uses and
    // a preferences list that reshuffles itself between domain reloads is unusable.
    public static string[] Roots {
      get {
        var extra = new List<string>(ExtraRoots);
        extra.Sort(StringComparer.Ordinal);

        var all = new List<string>(BuiltinRoots.Length + extra.Count);
        all.AddRange(BuiltinRoots);
        all.AddRange(extra);
        return all.ToArray();
      }
    }

    // Every menu path under one root, read from the live editor. Shared with
    // HasteMenuTree so the internal-API fallback lives in exactly one place.
    public static string[] ReadPaths(string root) {
      try {
        var paths = Reader(root);
        return paths ?? new string[0];
      } catch (Exception) {
        return new string[0];
      }
    }

    public static bool IsBuiltinRoot(string root) {
      return MatchName(BuiltinRoots, root) != null;
    }

    // The interned BuiltinRoots entry a path sits under, or null. Compared in place so
    // that weighting the ~500 stock menu items on every keystroke allocates nothing.
    public static string MatchBuiltinRoot(string path) {
      if (string.IsNullOrEmpty(path)) {
        return null;
      }

      for (int i = 0; i < BuiltinRoots.Length; i++) {
        var root = BuiltinRoots[i];

        // Either the whole path IS the root, or the root is followed by a separator --
        // "Editor" must not match the "Edit" menu.
        if (path.Length < root.Length) {
          continue;
        }
        if (path.Length > root.Length && path[root.Length] != '/') {
          continue;
        }
        if (String.CompareOrdinal(path, 0, root, 0, root.Length) == 0) {
          return root;
        }
      }

      return null;
    }

    static string MatchName(string[] names, string name) {
      if (string.IsNullOrEmpty(name)) {
        return null;
      }
      for (int i = 0; i < names.Length; i++) {
        if (String.Equals(names[i], name, StringComparison.Ordinal)) {
          return names[i];
        }
      }
      return null;
    }

    // Actions Haste implements itself. They are deliberately not real menu items, so
    // enumeration never returns them; HasteActions holds the implementations.
    static readonly string[] CustomMenuItems = {
      "Assets/Instantiate Prefab",

      "GameObject/Lock",
      "GameObject/Unlock",
      "GameObject/Activate",
      "GameObject/Deactivate",
      "GameObject/Reset Transform",
      "GameObject/Select Parent",
      "GameObject/Select Children",

      // Prefab. "GameObject/Reconnect to Prefab" used to be here; see the note in
      // HasteActions for why the modern prefab system makes it meaningless.
      "GameObject/Select Prefab",
      "GameObject/Revert to Prefab",
    };

    // Reads every menu path under one root. Resolved once per domain.
    delegate string[] MenuPathReader(string root);

    static MenuPathReader reader;
    static bool readerResolved;

    static MenuPathReader Reader {
      get {
        if (!readerResolved) {
          readerResolved = true;
          reader = ResolveReader();
        }
        return reader;
      }
    }

    static MenuPathReader ResolveReader() {
      // Preferred: UnityEditor.Menu.GetMenuItems(string, bool, bool) -> ScriptingMenuItem[].
      // Require the exact shape rather than casting and hoping -- a mismatch means
      // unavailable, and the public fallback below is genuinely equivalent.
      try {
        var menuType = HasteReflection.EditorAssembly.GetType("UnityEditor.Menu");
        if (menuType != null) {
          var method = menuType.GetMethod("GetMenuItems",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(string), typeof(bool), typeof(bool) }, null);

          if (method != null && method.ReturnType.IsArray) {
            var pathProperty = method.ReturnType.GetElementType().GetProperty("path");
            if (pathProperty != null && pathProperty.PropertyType == typeof(string)) {
              return root => {
                // includeSeparators: false, localized: false -- separators are not
                // actionable, and an index keyed on the display language would not
                // survive the user changing it.
                var items = method.Invoke(null, new object[] { root, false, false }) as Array;
                if (items == null) {
                  return new string[0];
                }
                var paths = new string[items.Length];
                for (int i = 0; i < items.Length; i++) {
                  paths[i] = pathProperty.GetValue(items.GetValue(i), null) as string;
                }
                return paths;
              };
            }
          }
        }
      } catch (Exception e) {
        // Deliberately Debug.LogWarning and not HasteDebug.Warn: the latter is
        // [Conditional("DEBUG")] and would compile out of exactly the builds where
        // knowing the internal API vanished matters most.
        Debug.LogWarning("Haste could not use UnityEditor.Menu.GetMenuItems (" +
          e.GetType().Name + "); falling back to Unsupported.GetSubmenus.");
      }

      return root => Unsupported.GetSubmenus(root);
    }

    // Roots that [MenuItem] attributes put items under, beyond the editor's own.
    //
    // Cached for the lifetime of the domain because finding them is expensive: there is
    // no API that lists menu roots, so the only way is to walk every loaded assembly's
    // types and methods looking for the attribute, which measures at ~120 ms on a stock
    // Unity 6 project. The cost cannot be filtered away by assembly name -- it is spread
    // across a hundred assemblies, and the "Services" root is declared by
    // UnityEditor.Purchasing and UnityEditor.UnityConnectModule, so excluding Unity's own
    // assemblies would lose a real menu. GetEnumerator therefore yields the built-in
    // roots first and pays this afterwards, once, with the common menus already indexed.
    static string[] extraRoots;

    static string[] ExtraRoots {
      get {
        if (extraRoots == null) {
          var roots = new HashSet<string>();
          AddAttributeRoots(roots);
          foreach (var builtin in BuiltinRoots) {
            roots.Remove(builtin);
          }

          extraRoots = new string[roots.Count];
          roots.CopyTo(extraRoots);
        }
        return extraRoots;
      }
    }

    // Only the root is taken -- the items under it come from the live tree, which is
    // authoritative about what actually exists and is already stripped of shortcut
    // suffixes.
    static void AddAttributeRoots(HashSet<string> roots) {
      foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
        if (IsFrameworkAssembly(assembly.FullName)) {
          continue;
        }

        IEnumerable<HasteTuple<MenuItem, MethodInfo>> attributes;
        try {
          attributes = HasteReflection.GetAttributesInAssembly<MenuItem>(assembly);
        } catch (Exception) {
          // A half-loaded assembly throws from GetTypes(). One bad neighbour must not
          // cost us every other assembly's menu items.
          continue;
        }

        foreach (var attribute in attributes) {
          var path = attribute.First.menuItem;
          if (string.IsNullOrEmpty(path)) {
            continue;
          }

          var separator = path.IndexOf('/');
          if (separator <= 0) {
            continue;
          }

          var root = path.Substring(0, separator);

          // CONTEXT/* are component context menus -- right-click on an Inspector header,
          // not reachable from the menu bar. internal:* are hidden from the menu tree.
          if (root == "CONTEXT" || root.StartsWith("internal:", StringComparison.Ordinal)) {
            continue;
          }

          roots.Add(root);
        }
      }
    }

    // Assemblies that cannot usefully declare a [MenuItem] and are expensive to walk.
    // Unity's own assemblies are deliberately NOT excluded: Unity packages introduce
    // roots of their own -- "Services" is one -- and skipping them loses those menus.
    static bool IsFrameworkAssembly(string fullName) {
      return fullName.StartsWith("Mono", StringComparison.Ordinal)
          || fullName.StartsWith("ICSharpCode", StringComparison.Ordinal)
          || fullName.StartsWith("System", StringComparison.Ordinal)
          || fullName.StartsWith("netstandard", StringComparison.Ordinal)
          || fullName.StartsWith("mscorlib", StringComparison.Ordinal)
          || fullName.StartsWith("nunit", StringComparison.Ordinal)
          || fullName.StartsWith("UnityScript", StringComparison.Ordinal);
    }

    static bool IsValid(string path) {
      if (string.IsNullOrEmpty(path)) {
        return false;
      }
      if (path[path.Length - 1] == '/') {
        return false;
      }
      return path != SelfMenuItem;
    }

    IEnumerable<HasteItem> Enumerate(MenuPathReader read, string[] roots, HashSet<string> seen) {
      foreach (var root in roots) {
        string[] paths;
        try {
          paths = read(root);
        } catch (Exception) {
          // One unreadable root must not cost every other root's menu items.
          continue;
        }

        if (paths == null) {
          continue;
        }

        foreach (var path in paths) {
          if (IsValid(path) && seen.Add(path)) {
            yield return new HasteItem(path, 0, NAME);
          }
        }
      }
    }

    public IEnumerator<HasteItem> GetEnumerator() {
      var read = Reader;
      var seen = new HashSet<string>();

      // The editor's own menus first. That is ~500 of the ~540 items and costs about a
      // millisecond, so the menus people actually search for are indexed immediately.
      foreach (var item in Enumerate(read, BuiltinRoots, seen)) {
        yield return item;
      }

      // Then the roots that packages and user scripts invented, which is where the
      // expensive attribute scan happens -- once per domain, and only after the above.
      foreach (var item in Enumerate(read, ExtraRoots, seen)) {
        yield return item;
      }

      // Haste's own actions last, so a real menu item of the same name wins the dedupe.
      foreach (var path in CustomMenuItems) {
        if (IsValid(path) && seen.Add(path)) {
          yield return new HasteItem(path, 0, NAME);
        }
      }
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }
  }
}
