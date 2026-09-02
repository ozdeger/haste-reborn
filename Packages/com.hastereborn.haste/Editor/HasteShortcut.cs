using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;

namespace Haste {

  // How Haste gets opened.
  //
  // This used to be [MenuItem("Window/Haste %k")], and users were told in the README to
  // edit this file to rebind it. Two things are wrong with that on Unity 6:
  //
  //   1. Unity 6 ships [MenuItem("Edit/Search/Search All... %k")] on its own Search
  //      window. "%k" is Ctrl+K on Windows/Linux and Cmd+K on macOS, so Haste was
  //      claiming a chord the editor already owns -- and the loser of that fight simply
  //      does not open, with no error to explain why.
  //   2. A shortcut baked into a MenuItem string is not rebindable. ShortcutManager is,
  //      and it puts Haste in Edit > Shortcuts alongside everything else.
  //
  // Ctrl/Cmd+Shift+K is free on both platforms: across every shortcut Unity declares in
  // 6000.0 and 6000.3, KeyCode.K appears only twice (both in the Animation module, both
  // without Action), the Action|Shift combination is only used for N and Mouse1, and no
  // MenuItem uses %#k or #%k.
  //
  // ShortcutModifiers.Action resolves to Cmd on macOS and Ctrl elsewhere at runtime, so
  // one declaration is correct on both platforms. Do not use ShortcutModifiers.Control --
  // that means the literal Ctrl key even on a Mac.
  //
  // See Documentation~/activation-design.md for the full design, including the
  // double-tap-Shift gesture that layers on top of this.
  public static class HasteShortcut {

    // Stable id. Renaming it silently resets any rebinding the user has made, because
    // ShortcutManager keys user overrides by id.
    public const string ShortcutId = "Haste/Open Haste";

    [Shortcut(ShortcutId, KeyCode.K, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
    public static void OpenShortcut() {
      Open();
    }

    // Kept for discoverability, but deliberately WITHOUT a "%k" suffix so there is exactly
    // one rebindable entry in Edit > Shortcuts rather than two competing bindings.
    //
    // The exact string also matters: HasteMenuItemSource skips the menu item whose path
    // equals "Window/Haste" so Haste does not index itself. Changing this string without
    // changing that filter would put Haste in its own search results.
    [MenuItem("Window/Haste")]
    public static void Open() {
      if (!HasteSettings.Enabled) {
        return;
      }
      HasteSpotlightWindow.Open();
    }

    [MenuItem("Window/Haste", true)]
    public static bool IsHasteEnabled() {
      return HasteSettings.Enabled;
    }
  }
}
