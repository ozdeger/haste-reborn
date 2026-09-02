using System;
using System.Collections.Generic;
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
    Menu      = 1 << 6,
    Tool      = 1 << 7,
    Layout    = 1 << 8,
    Texture   = 1 << 9,
    Audio     = 1 << 10,
    Animation = 1 << 11,
    Animator  = 1 << 12,
    Material  = 1 << 13,
    Model     = 1 << 14,
    Shader    = 1 << 15,
    Font      = 1 << 16,

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
          return HasteKind.Menu;
      }

      return FromExtension(item.pathLower);
    }

    // Extension -> kind, compared in place so that classifying every candidate during a
    // scoped search allocates nothing. The length check in Extension short-circuits almost
    // all of these before any character is read.
    static HasteKind FromExtension(string pathLower) {
      var start = ExtensionStart(pathLower);
      if (start < 0) {
        return HasteKind.Asset;
      }

      if (Is(pathLower, start, "prefab")) return HasteKind.Prefab;
      if (Is(pathLower, start, "unity")) return HasteKind.Scene;
      if (Is(pathLower, start, "cs")) return HasteKind.Script;
      if (Is(pathLower, start, "anim")) return HasteKind.Animation;
      if (Is(pathLower, start, "mat")) return HasteKind.Material;

      if (Is(pathLower, start, "controller") || Is(pathLower, start, "overridecontroller")) {
        return HasteKind.Animator;
      }

      if (Is(pathLower, start, "png") || Is(pathLower, start, "jpg") ||
          Is(pathLower, start, "jpeg") || Is(pathLower, start, "tga") ||
          Is(pathLower, start, "psd") || Is(pathLower, start, "gif") ||
          Is(pathLower, start, "bmp") || Is(pathLower, start, "tif") ||
          Is(pathLower, start, "tiff") || Is(pathLower, start, "exr") ||
          Is(pathLower, start, "hdr") || Is(pathLower, start, "webp") ||
          Is(pathLower, start, "svg")) {
        return HasteKind.Texture;
      }

      if (Is(pathLower, start, "wav") || Is(pathLower, start, "mp3") ||
          Is(pathLower, start, "ogg") || Is(pathLower, start, "aif") ||
          Is(pathLower, start, "aiff") || Is(pathLower, start, "flac") ||
          Is(pathLower, start, "m4a")) {
        return HasteKind.Audio;
      }

      if (Is(pathLower, start, "fbx") || Is(pathLower, start, "obj") ||
          Is(pathLower, start, "blend") || Is(pathLower, start, "dae") ||
          Is(pathLower, start, "3ds") || Is(pathLower, start, "max")) {
        return HasteKind.Model;
      }

      if (Is(pathLower, start, "shader") || Is(pathLower, start, "shadergraph") ||
          Is(pathLower, start, "compute") || Is(pathLower, start, "cginc") ||
          Is(pathLower, start, "hlsl")) {
        return HasteKind.Shader;
      }

      if (Is(pathLower, start, "ttf") || Is(pathLower, start, "otf")) {
        return HasteKind.Font;
      }

      return HasteKind.Asset;
    }

    // Index of the first character after the final "." of the file name, or -1.
    static int ExtensionStart(string pathLower) {
      var dot = pathLower.LastIndexOf('.');
      if (dot < 0 || dot == pathLower.Length - 1) {
        return -1;
      }
      if (dot < pathLower.LastIndexOf('/')) {
        return -1;
      }
      return dot + 1;
    }

    static bool Is(string pathLower, int start, string extension) {
      return pathLower.Length - start == extension.Length &&
        string.CompareOrdinal(pathLower, start, extension, 0, extension.Length) == 0;
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
        case HasteKind.Menu:      return "MENU";
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
        case HasteKind.Texture:   return "texture";
        case HasteKind.Audio:     return "audio";
        case HasteKind.Animation: return "animation";
        case HasteKind.Animator:  return "animator";
        case HasteKind.Material:  return "material";
        case HasteKind.Model:     return "model";
        case HasteKind.Shader:    return "shader";
        case HasteKind.Font:      return "font";
        case HasteKind.Menu | HasteKind.Tool: return "menu";
        case HasteKind.Menu:      return "menu";
      }
      return "";
    }

    // "?" is deliberately mapped to menu items rather than a settings kind of its own:
    // Unity 6 exposes exactly one settings menu item ("Edit/Project Settings..."), not one
    // per page, so a settings scope would hold a single row. Reaching individual pages
    // needs SettingsService, which is not part of this pass.
    //
    // A `tokenNames` array used to sit here listing the tokens "longest first". Nothing
    // read it -- TryParseToken's switch is the only parser -- so it was a second list to
    // keep in step with no way to notice when it drifted.
    public static bool TryParseToken(string token, out HasteKind kinds) {
      kinds = HasteKind.None;
      if (string.IsNullOrEmpty(token)) {
        return false;
      }

      switch (token.ToLowerInvariant()) {
        // "menu" is the name; "command" and "cmd" still parse because they were the
        // name until recently and are what fingers remember. They are not advertised.
        case ">":
        case "menu":
        case "cmd":
        case "command":  kinds = HasteKind.Menu | HasteKind.Tool; return true;
        case "#":
        case "component": kinds = HasteKind.Component; return true;
        case "?":
        case "setting":
        case "settings": kinds = HasteKind.Menu; return true;
        // No single-letter aliases for "asset" or "scene". "a" is equally a start for
        // anim, animator, audio and asset; "s" for scene, script, shader, sound and
        // sprite. Picking a winner would be arbitrary, and the loser would be unreachable.
        case "asset":    kinds = HasteKind.Asset; return true;
        case "p":
        case "prefab":   kinds = HasteKind.Prefab; return true;
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

        // Asset types. Aliases are generous on purpose: these are typed by hand, and
        // "audioclip" is what Unity calls it while "audio" is what people type.
        case "tex":
        case "texture":
        case "image":
        case "sprite":   kinds = HasteKind.Texture; return true;
        case "audio":
        case "audioclip":
        case "sound":    kinds = HasteKind.Audio; return true;
        case "anim":
        case "animation":
        case "clip":     kinds = HasteKind.Animation; return true;
        case "animator":
        case "controller": kinds = HasteKind.Animator; return true;
        case "mat":
        case "material": kinds = HasteKind.Material; return true;
        case "model":
        case "mesh":
        case "fbx":      kinds = HasteKind.Model; return true;
        case "shader":   kinds = HasteKind.Shader; return true;
        case "font":     kinds = HasteKind.Font; return true;
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

        // "t:<type>" -- the syntax Unity's own Project window search uses, so it is what
        // people already have in their fingers. The type name runs to the first space.
        if (word.Length == 1 && (word[0] == 't' || word[0] == 'T')) {
          var rest = query.Substring(colon + 1);
          var end = rest.IndexOf(' ');

          // The name must be terminated by a space before it counts.
          //
          // Committing as soon as the text parses looks right and is not: type "t:script"
          // and it would lock to SCENE at "t:s", because "s" is a valid alias. The same
          // trap swallows "t:animator" at "t:anim" and "t:audioclip" at "t:audio". Any
          // alias that is a prefix of a longer one -- and there are several -- makes the
          // longer one unreachable.
          //
          // Requiring the space costs nothing in practice: the chips insert one, and
          // typing a query after the tag produces one anyway.
          if (end < 0) {
            kinds = HasteKind.Any;
            return query;
          }

          if (TryParseToken(rest.Substring(0, end), out kinds)) {
            token = Label(kinds);
            return rest.Substring(end + 1).TrimStart();
          }

          kinds = HasteKind.Any;
          return query;
        }

        if (TryParseToken(word, out kinds)) {
          token = Label(kinds);
          return query.Substring(colon + 1).TrimStart();
        }
      }

      kinds = HasteKind.Any;
      return query;
    }

    // Whether a row gets the Hierarchy window's colour coding -- prefab blue,
    // broken-prefab red, dimmed when inactive.
    //
    // Only hierarchy rows do, and the check is on the SOURCE rather than on "is the
    // object a GameObject", which is the trap: AssetDatabase.LoadMainAssetAtPath returns
    // a GameObject for a .prefab, and a prefab ASSET's activeInHierarchy is false because
    // it is not in a scene at all. Tinting on that dimmed every prefab in the Project
    // results as though it were disabled.
    public static bool UsesHierarchyTint(HasteItem item) {
      return item != null && item.source == HasteHierarchySource.NAME;
    }

    // Every individual kind, in the order the preferences page lists them. Excludes None
    // and Any, which are masks rather than kinds.
    public static readonly HasteKind[] All = {
      HasteKind.Asset,
      HasteKind.Prefab,
      HasteKind.Scene,
      HasteKind.Script,
      HasteKind.Texture,
      HasteKind.Audio,
      HasteKind.Animation,
      HasteKind.Animator,
      HasteKind.Material,
      HasteKind.Model,
      HasteKind.Shader,
      HasteKind.Font,
      HasteKind.Hierarchy,
      HasteKind.Component,
      HasteKind.Menu,
      HasteKind.Tool,
      HasteKind.Layout,
    };

    // Narrows an already-built result set to a scope.
    //
    // Search filters at the index, but the recency list is not a search -- it is handed
    // over whole -- so it needs filtering after the fact or a scope has no effect on it.
    public static IHasteResult[] Filter(IHasteResult[] results, HasteKind kinds) {
      if (results == null) {
        return new IHasteResult[0];
      }
      if (kinds == HasteKind.Any) {
        return results;
      }

      var kept = new List<IHasteResult>(results.Length);
      for (int i = 0; i < results.Length; i++) {
        if (results[i] != null && Matches(kinds, results[i].Item)) {
          kept.Add(results[i]);
        }
      }
      return kept.ToArray();
    }

    public static bool Matches(HasteKind kinds, HasteItem item) {
      return kinds == HasteKind.Any || (kinds & Classify(item)) != 0;
    }
  }
}
