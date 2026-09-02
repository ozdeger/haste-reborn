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

    HideActions,        // Left arrow or Escape, while the actions pane is open
    ActionUp,
    ActionDown,
    RunAction,
  }

  // The palette's keyboard map, as a pure function.
  //
  // Separated from the window because it is the only part of the input path that can be
  // tested: UI Toolkit does not run headlessly, so a missing key binding otherwise
  // compiles, passes every test, and is only found by someone pressing the key. That is
  // exactly how the right arrow shipped doing nothing.
  public static class HasteKeyMap {

    public static HasteKeyIntent Resolve(
      KeyCode key, bool actionKey, bool shift,
      bool actionsMode, bool hasScope, bool queryIsEmpty) {

      // The actions pane owns the keyboard entirely while it is open.
      if (actionsMode) {
        switch (key) {
          case KeyCode.UpArrow:     return HasteKeyIntent.ActionUp;
          case KeyCode.DownArrow:   return HasteKeyIntent.ActionDown;
          case KeyCode.LeftArrow:
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
