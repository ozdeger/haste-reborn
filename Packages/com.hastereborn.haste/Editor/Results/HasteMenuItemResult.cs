using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace Haste {

  public class HasteMenuItemResult : AbstractHasteResult {

    public HasteMenuItemResult(HasteItem item, float score, string[] terms) : base(item, score, terms) {}

    public override void Action() {
      HasteActions.MenuItemFallbackDelegate menuItemFallback;
      if (HasteActions.MenuItemFallbacks.TryGetValue(Item.path, out menuItemFallback)) {
        try {
          menuItemFallback();
        } catch (NotImplementedException ex) {
          Debug.LogException(ex);
        }
      } else {
        EditorApplication.ExecuteMenuItem(Item.path);
      }
    }
  }
}
