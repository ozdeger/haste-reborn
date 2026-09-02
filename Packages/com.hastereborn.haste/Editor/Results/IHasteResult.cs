using System;

namespace Haste {

  public interface IHasteResult : IComparable<IHasteResult> {
    HasteItem Item { get; }
    float Score { get; }

    bool IsVisible { get; set; }

    int[] Indices { get; }

    // The query, split into the terms that all had to match. The row renders the name and
    // the directory separately, and each needs its own highlight positions.
    string[] Terms { get; }

    bool IsDraggable { get; }
    UnityEngine.Object Object { get; }
    string DragLabel { get; }

    bool Validate();

    // Enter. Reveals the thing: focuses the window that owns it, selects it, pings it.
    // For a menu item there is nothing to reveal, so this runs it.
    void Action();

    // Shift+Enter. Opens the thing in whatever edits it -- a script in the IDE, a scene
    // in the editor, a prefab in Prefab Mode. Falls back to Action where opening means
    // nothing, so it is always safe to call.
    void Open();

    // Whether Open does something other than fall back, which is what decides if the
    // actions pane offers it at all.
    bool CanOpen { get; }

    void Select();
  }
}
