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
  // The default is a bare Tab, because a palette you reach for constantly should cost one
  // key. It replaced Ctrl/Cmd+Shift+K, which was free but needed three fingers, and the
  // double-tap-Shift gesture that used to sit alongside it -- see the CHANGELOG for 2.6.0
  // and Documentation~/activation-design.md for why the gesture went.
  //
  // Tab is a legal binding, which is worth stating because most keys that feel like this
  // one are not: BindingValidator.s_InvalidKeyCodes on 6000.3.17f1 is exactly
  // { None, Escape, Return, CapsLock, and the ten modifier keycodes }, and Tab is in
  // none of it. A rejected binding does not fail loudly -- the id registers with an EMPTY
  // binding and only a discovery warning is logged -- so HasteActivationTests asserts on
  // the registered binding rather than on this file compiling.
  //
  // It is also effectively unclaimed. Across the 749 shortcut ids a full editor registers,
  // Tab appears exactly once more: Timeline/ToggleClipTrackArea, which is declared with
  // typeof(TimelineWindow) as its context and therefore only applies while the Timeline
  // window has focus. Haste's is global, so Timeline wins inside Timeline and Haste wins
  // everywhere else, which is the right way round.
  //
  // No modifiers is deliberate and is passed explicitly rather than left to the default,
  // so that the intent is in the attribute and a test can read it back.
  public static class HasteShortcut {

    // Stable id. Renaming it silently resets any rebinding the user has made, because
    // ShortcutManager keys user overrides by id.
    public const string ShortcutId = "Haste/Open Haste";

    [Shortcut(ShortcutId, KeyCode.Tab, ShortcutModifiers.None)]
    public static void OpenShortcut() {
      Open();
    }

    // Kept for discoverability, but deliberately WITHOUT a shortcut suffix so there is
    // exactly one rebindable entry in Edit > Shortcuts rather than two competing bindings.
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

    // What to call the shortcut in the UI. Reads the LIVE binding, so a rebinding in
    // Edit > Shortcuts is reflected in the hints instead of the window confidently naming
    // a chord that no longer opens it.
    //
    // ShortcutBinding.ToString() already renders platform-correctly (the Mac gets its
    // glyphs), and an unbound shortcut renders as the empty string -- which is why the
    // fallback below exists rather than letting a blank hint through.
    public static string Label {
      get {
        try {
          var binding = ShortcutManager.instance.GetShortcutBinding(ShortcutId);
          var text = binding.ToString();
          if (!string.IsNullOrEmpty(text)) {
            return text;
          }
        } catch (System.ArgumentException) {
          // Unknown id: the attribute was rejected at discovery. Fall through.
        }
        return Application.platform == RuntimePlatform.OSXEditor ? "⇥" : "Tab";
      }
    }
  }
}
