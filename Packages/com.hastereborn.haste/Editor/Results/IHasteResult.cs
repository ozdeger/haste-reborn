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
    bool IsSelected { get; }
    UnityEngine.Object Object { get; }
    string DragLabel { get; }

    void Draw(bool isHighlighted);
    float Height(bool isHighlighted);
    bool Validate();
    void Action();
    void Select();
  }
}
