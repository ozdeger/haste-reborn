namespace Haste {

  // What a click on a result row means.
  public enum HasteClickIntent {
    // Move the cursor to the row and nothing else.
    Highlight,

    // Highlight it and show it where it lives. The palette stays open.
    Reveal,

    // Open it in whatever edits it. The palette closes.
    Open,

    // Cmd/Ctrl+click: add the row to, or remove it from, the multi-selection.
    ToggleMultiSelect,
  }

  // The palette's mouse map, as a pure function.
  //
  // Split out for the same reason as HasteKeyMap: UI Toolkit does not run headlessly, so
  // the click handler itself cannot be tested, and a wrong branch there compiles, passes
  // every test, and is found only by someone clicking. Everything that decides is here;
  // the handler is left with nothing but the dispatch.
  public static class HasteMouseMap {

    // hasObject:       whether the row points at something that can be shown. Menu items
    //                  and window layouts do not -- there is no asset to ping.
    // multiSelecting:  whether a Cmd/Ctrl+click multi-selection is already in progress.
    public static HasteClickIntent Resolve(
      int clickCount, bool actionKey, bool hasObject, bool multiSelecting) {

      // First, so the modifier wins over the click count rather than racing it. A
      // Cmd+double-click is still two toggles, which is what it looks like it should be.
      if (actionKey) {
        return HasteClickIntent.ToggleMultiSelect;
      }

      // Double-click opens, whether or not the row has an object: IHasteResult.Open falls
      // back to Action where opening means nothing, so a menu row runs and a layout
      // switches, exactly as Enter does on them.
      if (clickCount >= 2) {
        return HasteClickIntent.Open;
      }

      // A single click on a row with nothing to show only moves the cursor. The
      // alternative is running the thing, and a menu item is not something to run because
      // the mouse passed over it -- "Delete" is one click away in the same list.
      if (!hasObject) {
        return HasteClickIntent.Highlight;
      }

      // Revealing sets Selection.objects to the one row, which would throw away a
      // multi-selection that is still being assembled. The cursor still moves.
      if (multiSelecting) {
        return HasteClickIntent.Highlight;
      }

      return HasteClickIntent.Reveal;
    }
  }
}
