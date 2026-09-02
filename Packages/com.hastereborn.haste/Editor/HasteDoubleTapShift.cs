using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // Opens the palette on a double tap of Shift.
  //
  // Tier 1 of Documentation~/activation-design.md: an extra, never the only way in. The
  // Ctrl/Cmd+Shift+K shortcut is attribute-registered and unaffected by anything here, so
  // if every hook below is unavailable the tool still opens.
  //
  // ShortcutManager cannot express this. BindingValidator.s_InvalidKeyCodes contains every
  // modifier keycode, and a malformed [Shortcut] does not fail loudly -- it registers the
  // id with an EMPTY binding and logs a discovery warning. Hence the reflection.
  [InitializeOnLoad]
  public static class HasteDoubleTapShift {

    // beforeEventProcessed is PRE-consumption: it is invoked in GUIUtility.ProcessEvent
    // after the event is copied from native and before anything dispatches it. That
    // matters because a focused text field consumes the letter between two Shift taps but
    // passes the Shifts through -- so on the post-consumption hook
    // (EditorApplication.globalEventHandler) the "any other key resets" rule would be a
    // no-op exactly while someone types CamelCase.
    const string BeforeEventProcessedField = "beforeEventProcessed";

    static readonly HasteDoubleTapShiftGesture gesture = new HasteDoubleTapShiftGesture();

    static bool hooked;
    static int consecutiveFailures;
    static bool disabledPermanently;

    static Action<EventType, KeyCode, EventModifiers> handler;

    // Diagnostics state. Only touched when the preference is on.
    static EventModifiers lastSeenModifiers;
    static int diagnosticLines;
    static bool modifierWatchHooked;

    static HasteDoubleTapShift() {
      // [InitializeOnLoad] runs once per domain and statics reset with it, so a
      // cross-reload duplicate is structurally impossible. `hooked` guards within a domain.
      Hook();
      HookModifierWatch();
    }

    static void Hook() {
      if (hooked || disabledPermanently) {
        return;
      }

      FieldInfo field;
      try {
        field = typeof(GUIUtility).GetField(BeforeEventProcessedField,
          BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        // A type mismatch means unavailable, not cast-and-pray.
        if (field == null || field.FieldType != typeof(Action<EventType, KeyCode, EventModifiers>)) {
          Degrade("GUIUtility." + BeforeEventProcessedField + " is missing or changed shape");
          return;
        }
      } catch (Exception e) {
        Degrade("could not resolve GUIUtility." + BeforeEventProcessedField + ": " + e.GetType().Name);
        return;
      }

      try {
        handler = OnBeforeEventProcessed;

        // Combine, never assign. Assigning would wipe Unity's own subscribers -- including
        // ShortcutIntegration's state reset -- which is the failure mode careless plugins
        // actually ship.
        var existing = (Action<EventType, KeyCode, EventModifiers>)field.GetValue(null);
        field.SetValue(null, (Action<EventType, KeyCode, EventModifiers>)
          Delegate.Combine(existing, handler));

        hooked = true;
      } catch (Exception e) {
        Degrade("could not subscribe: " + e.GetType().Name);
      }
    }

    // EditorApplication.modifierKeysChanged is public and parameterless: it says "some
    // modifier changed" and nothing else. Useless on its own, but it is the one signal
    // that is documented to fire for a bare modifier press, so the diagnostics report
    // whether it fires at all -- which is the difference between "we can build a fallback"
    // and "macOS never tells us".
    static void HookModifierWatch() {
      if (modifierWatchHooked) {
        return;
      }
      modifierWatchHooked = true;

      try {
        EditorApplication.modifierKeysChanged += OnModifierKeysChanged;
      } catch (Exception) {
        modifierWatchHooked = false;
      }
    }

    static void OnModifierKeysChanged() {
      if (!HasteSettings.DoubleTapShiftDiagnostics) {
        return;
      }
      Log("modifierKeysChanged");
    }

    static void Log(string message) {
      // Capped so a stuck state cannot bury the console.
      if (diagnosticLines++ > 300) {
        return;
      }
      Debug.Log(string.Format("[Haste] {0}  t={1:0.000}", message, EditorApplication.timeSinceStartup));
    }

    // What the hook actually delivers.
    //
    // Layout, Repaint and MouseMove flood, so they are logged only when the modifier bits
    // CHANGED since the last event -- which is the interesting case: if macOS never sends
    // a bare Shift as KeyDown, the press may still show up as a modifier bit riding on the
    // next event of any type, and that would be enough to build on.
    static void LogEvent(EventType type, KeyCode key, EventModifiers modifiers) {
      var changed = modifiers != lastSeenModifiers;
      var previous = lastSeenModifiers;
      lastSeenModifiers = modifiers;

      var noisy = type == EventType.Layout || type == EventType.Repaint ||
                  type == EventType.MouseMove || type == EventType.MouseDrag;

      if (noisy && !changed) {
        return;
      }

      Log(string.Format("{0,-12} key={1,-12} mods={2}{3}",
        type, key, modifiers, changed ? "  (was " + previous + ")" : ""));
    }

    static void Unhook() {
      if (!hooked) {
        return;
      }
      hooked = false;

      try {
        var field = typeof(GUIUtility).GetField(BeforeEventProcessedField,
          BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null) {
          var existing = (Action<EventType, KeyCode, EventModifiers>)field.GetValue(null);
          field.SetValue(null, (Action<EventType, KeyCode, EventModifiers>)
            Delegate.Remove(existing, handler));
        }
      } catch (Exception) {
        // Nothing useful to do; the guard below stops us running either way.
      }
    }

    static void Degrade(string reason) {
      disabledPermanently = true;
      Debug.LogWarning("[Haste] Double-tap Shift is unavailable (" + reason +
        "). Ctrl/Cmd+Shift+K still opens Haste.");
    }

    // This runs inside Unity's own event dispatch, first in the multicast list, because
    // ShortcutIntegration attaches lazily via delayCall. An exception escaping here aborts
    // the remaining invocations and kills every shortcut in the editor -- presenting as a
    // Unity bug. Nothing may escape.
    static void OnBeforeEventProcessed(EventType type, KeyCode key, EventModifiers modifiers) {
      if (disabledPermanently) {
        return;
      }

      try {
        if (!HasteSettings.DoubleTapShiftEnabled) {
          gesture.Reset();
          return;
        }

        if (HasteSettings.DoubleTapShiftDiagnostics) {
          LogEvent(type, key, modifiers);
        }

        gesture.WindowSeconds = HasteSettings.DoubleTapShiftWindowMs / 1000.0;

        if (gesture.Feed(type, key, modifiers, EditorApplication.timeSinceStartup, IsSuppressed())) {
          // Deferred: opening a window during event dispatch corrupts Unity's layout state.
          EditorApplication.delayCall += HasteSpotlightWindow.Open;
        }

        if (gesture.TrippedBreaker) {
          Degrade("it fired repeatedly in a few seconds, which means a false positive. " +
            "Re-enable it in Preferences > Haste");
        }

        consecutiveFailures = 0;
      } catch (Exception e) {
        gesture.Reset();
        consecutiveFailures++;

        if (consecutiveFailures >= 2) {
          Unhook();
          Degrade("it threw twice in a row: " + e.GetType().Name);
        }
      }
    }

    // Everything the gesture itself cannot know. Suppressing while a text field is being
    // edited removes the largest false-positive class outright -- Hierarchy renames,
    // Inspector fields, search boxes, Haste's own query field, and IME input where a bare
    // Shift is a mode toggle.
    static bool IsSuppressed() {
      if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) {
        return true;
      }

      if (Haste.IsApplicationBusy) {
        return true;
      }

      if (EditorGUIUtility.editingTextField || EditorGUIUtility.textFieldHasSelection) {
        return true;
      }

      // A drag in progress: the Shift is snapping something, not asking for a palette.
      if (GUIUtility.hotControl != 0) {
        return true;
      }

      // Already open; a second gesture should not stack windows.
      return HasteSpotlightWindow.IsOpen;
    }
  }
}
