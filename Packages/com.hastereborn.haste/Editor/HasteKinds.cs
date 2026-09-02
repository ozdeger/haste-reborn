using System;
using UnityEngine;

namespace Haste {

  // What an indexed item is, for the row's type badge and for scope tokens.
  //
  // This is a presentation taxonomy, not the index's own. Haste indexes four SOURCES
  // (Hierarchy, Project, Menu Item, Layout); the design asks for finer buckets than that,
  // splitting Project by file type and Menu Item by menu root. Flags rather than a plain
  // enum because one token can name several kinds -- ">" means commands and tools alike.
  [Flags]
  public enum HasteKind {
    None      = 0,
    Asset     = 1 << 0,
    Prefab    = 1 << 1,
    Scene     = 1 << 2,
    Script    = 1 << 3,
    Hierarchy = 1 << 4,
    Component = 1 << 5,
    Command   = 1 << 6,
    Tool      = 1 << 7,
    Layout    = 1 << 8,

    Any       = ~0,
  }

  public static class HasteKinds {

    // Classifies without allocating: EndsWith and StartsWith compare in place, so this is
    // cheap enough to run over every candidate when a scope token is active, and there is
    // no cached field on HasteItem to keep in step.
    public static HasteKind Classify(HasteItem item) {
      if (item == null) {
        return HasteKind.None;
      }

      switch (item.source) {
        case HasteHierarchySource.NAME:
          return HasteKind.Hierarchy;

        case HasteLayoutSource.NAME:
          return HasteKind.Layout;

        case HasteMenuItemSource.NAME:
          if (item.path.StartsWith("Component/", StringComparison.Ordinal)) {
            return HasteKind.Component;
          }
          if (item.path.StartsWith("Tools/", StringComparison.Ordinal)) {
            return HasteKind.Tool;
          }
          return HasteKind.Command;
      }

      var path = item.pathLower;
      if (path.EndsWith(".prefab", StringComparison.Ordinal)) {
        return HasteKind.Prefab;
      }
      if (path.EndsWith(".unity", StringComparison.Ordinal)) {
        return HasteKind.Scene;
      }
      if (path.EndsWith(".cs", StringComparison.Ordinal)) {
        return HasteKind.Script;
      }
      return HasteKind.Asset;
    }

    // The short badge shown at the left of a row. Generic assets use their own extension,
    // as in the design ("PNG", "MAT"); everything else has a fixed label.
    public static string Tag(HasteItem item) {
      var kind = Classify(item);
      switch (kind) {
        case HasteKind.Prefab:    return "PRE";
        case HasteKind.Scene:     return "SCN";
        case HasteKind.Script:    return "CS";
        case HasteKind.Hierarchy: return "GO";
        case HasteKind.Component: return "CMP";
        case HasteKind.Command:   return "CMD";
        case HasteKind.Tool:      return "TL";
        case HasteKind.Layout:    return "LAY";
      }

      var extension = HasteStringUtils.GetExtension(item.path);
      if (extension.Length == 0) {
        return "ASS";
      }
      if (extension.Length > 4) {
        extension = extension.Substring(0, 4);
      }
      return extension.ToUpperInvariant();
    }

    // The word shown in the scope chip once a token is committed.
    public static string Label(HasteKind kind) {
      switch (kind) {
        case HasteKind.Asset:     return "asset";
        case HasteKind.Prefab:    return "prefab";
        case HasteKind.Scene:     return "scene";
        case HasteKind.Script:    return "script";
        case HasteKind.Hierarchy: return "hierarchy";
        case HasteKind.Component: return "component";
        case HasteKind.Tool:      return "tool";
        case HasteKind.Layout:    return "layout";
        case HasteKind.Command | HasteKind.Tool: return "command";
        case HasteKind.Command:   return "command";
      }
      return "";
    }

    // Tokens the query can start with, longest first so "script" wins over "s".
    //
    // "?" is deliberately mapped to commands rather than a settings kind of its own:
    // Unity 6 exposes exactly one settings menu item ("Edit/Project Settings..."), not one
    // per page, so a settings scope would hold a single row. Reaching individual pages
    // needs SettingsService, which is not part of this pass.
    static readonly string[] tokenNames = {
      "hierarchy", "component", "prefab", "script", "layout", "asset", "scene",
      "cmd", "tool", "cs", "go", "go", "a", "p", "s", "h", "l",
    };

    public static bool TryParseToken(string token, out HasteKind kinds) {
      kinds = HasteKind.None;
      if (string.IsNullOrEmpty(token)) {
        return false;
      }

      switch (token.ToLowerInvariant()) {
        case ">":
        case "cmd":
        case "command":  kinds = HasteKind.Command | HasteKind.Tool; return true;
        case "#":
        case "component": kinds = HasteKind.Component; return true;
        case "?":
        case "setting":
        case "settings": kinds = HasteKind.Command; return true;
        case "a":
        case "asset":    kinds = HasteKind.Asset; return true;
        case "p":
        case "prefab":   kinds = HasteKind.Prefab; return true;
        case "s":
        case "scene":    kinds = HasteKind.Scene; return true;
        case "cs":
        case "script":   kinds = HasteKind.Script; return true;
        case "h":
        case "go":
        case "hierarchy": kinds = HasteKind.Hierarchy; return true;
        case "tool":
        case "tools":    kinds = HasteKind.Tool; return true;
        case "l":
        case "layout":   kinds = HasteKind.Layout; return true;
      }
      return false;
    }

    // Splits a leading scope token off a raw query.
    //
    // "prefab:foo" and ">build" become (kinds, "foo") and (commands, "build"). A query
    // with no token comes back unchanged with kinds = Any, so callers can treat the
    // scoped and unscoped cases identically.
    public static string SplitScope(string query, out HasteKind kinds, out string token) {
      kinds = HasteKind.Any;
      token = null;

      if (string.IsNullOrEmpty(query)) {
        return query;
      }

      // Single-character sigils bind immediately; word tokens need their colon.
      var head = query[0];
      if (head == '>' || head == '#' || head == '?') {
        if (TryParseToken(head.ToString(), out kinds)) {
          token = Label(kinds);
          return query.Substring(1).TrimStart();
        }
      }

      var colon = query.IndexOf(':');
      if (colon > 0) {
        var word = query.Substring(0, colon);
        if (TryParseToken(word, out kinds)) {
          token = Label(kinds);
          return query.Substring(colon + 1).TrimStart();
        }
      }

      kinds = HasteKind.Any;
      return query;
    }

    public static bool Matches(HasteKind kinds, HasteItem item) {
      return kinds == HasteKind.Any || (kinds & Classify(item)) != 0;
    }
  }
}
