using UnityEngine;

namespace Haste {

  // What a keystroke means in the palette.
  public enum HasteKeyIntent {
    None,

    Dismiss,
    Reveal,             // Enter
    Open,               // Shift+Enter
    ToggleMultiSelect,  // Cmd/Ctrl+Enter
    MoveUp,
    MoveDown,
    MoveHome,
    MoveEnd,
    MovePageUp,
    MovePageDown,
    ShowActions,        // Right arrow, or Cmd/Ctrl+K
    ClearScope,

    HideActions,        // Escape, or the left arrow at the top level of the pane
    ActionUp,
    ActionDown,
    RunAction,
    EnterSubmenu,       // Right arrow on a row that opens a submenu
    LeaveSubmenu,       // Left arrow anywhere below the top level
  }

  // The palette's keyboard map, as a pure function.
  //
  // Separated from the window because it is the only part of the input path that can be
  // tested: UI Toolkit does not run headlessly, so a missing key binding otherwise
  // compiles, passes every test, and is only found by someone pressing the key. That is
  // exactly how the right arrow shipped doing nothing.
  public static class HasteKeyMap {

    // actionsAtRoot: whether the pane is showing the item's top-level menu rather than a
    // submenu of it. It decides what the left arrow means, and it is passed in rather than
    // inferred because a context menu nests arbitrarily deep.
    public static HasteKeyIntent Resolve(
      KeyCode key, bool actionKey, bool shift,
      bool actionsMode, bool hasScope, bool queryIsEmpty, bool actionsAtRoot) {

      // The actions pane owns the keyboard entirely while it is open.
      if (actionsMode) {
        switch (key) {
          case KeyCode.UpArrow:     return HasteKeyIntent.ActionUp;
          case KeyCode.DownArrow:   return HasteKeyIntent.ActionDown;

          // Right goes deeper, mirroring the way it opened the pane in the first place.
          // Enter on a submenu row does the same -- RunAction resolves that -- because a
          // submenu is not something that can be run.
          case KeyCode.RightArrow:  return HasteKeyIntent.EnterSubmenu;

          // Left retraces one level and only closes the pane once there is nothing left
          // to retrace. Escape always closes the whole pane, which is what Unity's own
          // menus do and what makes the two keys worth having separately.
          case KeyCode.LeftArrow:
            return actionsAtRoot ? HasteKeyIntent.HideActions : HasteKeyIntent.LeaveSubmenu;

          case KeyCode.Escape:      return HasteKeyIntent.HideActions;
          case KeyCode.Return:
          case KeyCode.KeypadEnter: return HasteKeyIntent.RunAction;
        }
        return HasteKeyIntent.None;
      }

      // Cmd/Ctrl+K opens the pane as well as the right arrow, which is what the design
      // shows. It does not collide with the chord that opens Haste: the palette already
      // has focus by the time this runs.
      if (key == KeyCode.K && actionKey) {
        return HasteKeyIntent.ShowActions;
      }

      switch (key) {
        case KeyCode.Escape:      return HasteKeyIntent.Dismiss;
        case KeyCode.RightArrow:  return HasteKeyIntent.ShowActions;
        case KeyCode.UpArrow:     return HasteKeyIntent.MoveUp;
        case KeyCode.DownArrow:   return HasteKeyIntent.MoveDown;
        case KeyCode.Home:        return HasteKeyIntent.MoveHome;
        case KeyCode.End:         return HasteKeyIntent.MoveEnd;
        case KeyCode.PageUp:      return HasteKeyIntent.MovePageUp;
        case KeyCode.PageDown:    return HasteKeyIntent.MovePageDown;

        case KeyCode.Return:
        case KeyCode.KeypadEnter:
          if (actionKey) {
            return HasteKeyIntent.ToggleMultiSelect;
          }
          return shift ? HasteKeyIntent.Open : HasteKeyIntent.Reveal;

        case KeyCode.Backspace:
          // Only once there is nothing left to delete, so backspace still edits text.
          return hasScope && queryIsEmpty ? HasteKeyIntent.ClearScope : HasteKeyIntent.None;
      }

      return HasteKeyIntent.None;
    }
  }
}
