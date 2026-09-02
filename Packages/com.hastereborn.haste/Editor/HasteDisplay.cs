using UnityEditor;
using UnityEngine;

namespace Haste {

  // Where to put the palette when it opens.
  public static class HasteDisplay {

    // The main editor window's rectangle in screen space.
    //
    // The palette used to be centred with Screen.currentResolution, which reports the
    // PRIMARY display regardless of where Unity actually is -- so on a second monitor the
    // palette opened on the wrong screen, and on a scaled display it was off-centre even
    // on the right one. EditorGUIUtility.GetMainWindowPosition answers the question that
    // was actually being asked.
    public static Rect MainWindowArea() {
      var main = EditorGUIUtility.GetMainWindowPosition();

      // A zero-sized rect means Unity has no main window yet (very early startup).
      if (main.width > 1f && main.height > 1f) {
        return main;
      }

      var resolution = Screen.currentResolution;
      return new Rect(0f, 0f, resolution.width, resolution.height);
    }
  }
}
