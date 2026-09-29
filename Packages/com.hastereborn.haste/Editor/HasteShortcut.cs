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
  // The default is one bare key: the one above Tab, left of 1. A palette you reach for
  // constantly should cost one key, and this one types nothing anyone needs mid-flow.
  // It replaced a bare Tab (2.6.0-2.7.x), which replaced Ctrl/Cmd+Shift+K; see the
  // CHANGELOG, and Documentation~/activation-design.md for the removed Shift gesture.
  //
  // WHICH KeyCode that is depends on the platform, and it is the one part of this that
  // was not measured. macOS reports punctuation by the character it types, so on a
  // Turkish-Q keyboard -- the one this was asked for on -- the key arrives as '"',
  // DoubleQuote. Windows reports the physical key, VK_OEM_3, which Unity names BackQuote
  // on every common layout. A US Mac types '`' there and so gets nothing from this
  // default; that is what Edit > Shortcuts is for, and rebinding by pressing the key
  // records whatever the platform actually reports.
  //
  // Both are legal bindings: BindingValidator.s_InvalidKeyCodes on 6000.3.17f1 is exactly
  // { None, Escape, Return, CapsLock, and the ten modifier keycodes }. A rejected binding
  // does not fail loudly -- the id registers with an EMPTY binding and only a discovery
  // warning is logged -- so HasteActivationTests asserts on the declared key rather than
  // on this file compiling.
  //
  // No modifiers is deliberate and is passed explicitly rather than left to the default,
  // so that the intent is in the attribute and a test can read it back.
  public static class HasteShortcut {

    // Stable id. Renaming it silently resets any rebinding the user has made, because
    // ShortcutManager keys user overrides by id.
    public const string ShortcutId = "Haste/Open Haste";

#if UNITY_EDITOR_OSX
    public const KeyCode DefaultKey = KeyCode.DoubleQuote;
#else
    public const KeyCode DefaultKey = KeyCode.BackQuote;
#endif

    [Shortcut(ShortcutId, DefaultKey, ShortcutModifiers.None)]
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

    // The character the shortcut's key types, when it types one -- or null.
    //
    // Only for a bare key: with a modifier held the key is a chord, not typing. And only
    // for the printable-ASCII range, where a KeyCode's value IS its character (A is 97,
    // DoubleQuote 34, BackQuote 96); anything outside it types nothing we can predict.
    // Reads the live binding, so a rebinding to another bare key is handled the same way.
    public static char? TypedCharacter {
      get {
        try {
          var combos = ShortcutManager.instance.GetShortcutBinding(ShortcutId).keyCombinationSequence;
          foreach (var combo in combos) {
            var code = (int)combo.keyCode;
            if (combo.modifiers != ShortcutModifiers.None || code < 33 || code > 126) {
              return null;
            }
            return (char)code;
          }
        } catch (System.ArgumentException) {
          // Unknown id; nothing to protect.
        }
        return null;
      }
    }

    // Whether an edit to the query only typed the shortcut's own character into an empty
    // field -- the key that opened the palette leaking into it.
    //
    // A bare printable key opens the palette on KeyDown, so the same key's repeats while
    // it is held, the press itself if the field is focused before the key is handled,
    // and every press after the palette is already open all arrive as TEXT. Guarding the
    // value rather than the key event is deliberate: it catches all of those the same way,
    // and does not depend on whether stopping a KeyDown in trickle-down stops UI Toolkit's
    // text field from inserting the character -- which cannot be tested headlessly.
    //
    // Only while the field is empty. Once anything else has been typed, the character is
    // just a character; the rule is that the query cannot START with it. Pasting "\"foo"
    // is not only the character, so it goes through.
    public static bool IsOnlyTheShortcutCharacter(string before, string after, char? typed) {
      if (!typed.HasValue || !string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after)) {
        return false;
      }
      var c = char.ToLowerInvariant(typed.Value);
      foreach (var ch in after) {
        if (char.ToLowerInvariant(ch) != c) {
          return false;
        }
      }
      return true;
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
        return DefaultKey == KeyCode.DoubleQuote ? "\"" : "`";
      }
    }
  }
}
