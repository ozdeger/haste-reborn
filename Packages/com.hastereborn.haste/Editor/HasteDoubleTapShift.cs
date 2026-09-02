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
    //
    // It is also the only hook that sees the gesture at all on macOS. Measured there on
    // 6000.3.17f1: a bare Shift produces NO key event, and shows up purely as the modifier
    // bits on the next event of any type -- a Repaint, one millisecond later. The gesture
    // therefore reads modifier transitions rather than KeyDown/KeyUp.
    const string BeforeEventProcessedField = "beforeEventProcessed";

    static readonly HasteDoubleTapShiftGesture gesture = new HasteDoubleTapShiftGesture();

    static bool hooked;
    static int consecutiveFailures;
    static bool disabledPermanently;

    static Action<EventType, KeyCode, EventModifiers> handler;

    // Diagnostics state. Only touched when the preference is on.
    static EventModifiers lastSeenModifiers;
    static bool lastShiftBit;
    static bool lastDiagnosticsSetting;
    static bool announcedCap;
    static bool announcedDisabled;
    static bool lastFedShiftBit;
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

    // Re-arms the line cap when the preference is switched on, and explains itself if
    // the gesture is already disabled -- otherwise turning logging on after a degrade
    // produces silence and no reason for it.
    static void SyncDiagnostics() {
      var on = HasteSettings.DoubleTapShiftDiagnostics;
      if (on == lastDiagnosticsSetting) {
        return;
      }
      lastDiagnosticsSetting = on;

      if (!on) {
        return;
      }

      diagnosticLines = 0;
      announcedCap = false;

      if (disabledPermanently && !announcedDisabled) {
        announcedDisabled = true;
        Debug.LogWarning("[Haste] Key logging is on, but double-tap Shift is currently " +
          "disabled (it degraded earlier this session). Events are still logged. Use " +
          "\"Reset double-tap state\" in Preferences > Haste to re-enable the gesture.");
      }
    }

    // Clears a degrade so the gesture can be tried again without a domain reload. There
    // was previously no way back at all.
    public static void ResetState() {
      disabledPermanently = false;
      announcedDisabled = false;
      announcedCap = false;
      diagnosticLines = 0;
      consecutiveFailures = 0;
      gesture.ClearBreaker();
      gesture.Reset();
      Hook();
    }

    public static bool IsDisabled {
      get { return disabledPermanently; }
    }

    static void OnModifierKeysChanged() {
      if (!HasteSettings.DoubleTapShiftDiagnostics) {
        return;
      }
      Log("modifierKeysChanged");
    }

    // Capped so a stuck state cannot bury the console -- but it SAYS SO when it stops.
    // The first version just went quiet at 300 lines, which reads exactly like the hook
    // having died.
    const int MaxDiagnosticLines = 2000;

    static void Log(string message) {
      if (diagnosticLines >= MaxDiagnosticLines) {
        if (!announcedCap) {
          announcedCap = true;
          Debug.LogWarning("[Haste] Key logging reached " + MaxDiagnosticLines +
            " lines and stopped. Toggle it off and on again in Preferences > Haste to " +
            "start a fresh run.");
        }
        return;
      }

      diagnosticLines++;
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

      // Only what bears on the gesture: a modifier change, or a key event.
      //
      // The first version logged every event type that was not Layout/Repaint/MouseMove/
      // MouseDrag, which still leaves MouseEnterWindow, MouseLeaveWindow, ValidateCommand,
      // ExecuteCommand, ContextClick and Used -- all of which flood during ordinary editor
      // use. It exhausted its own line cap within seconds and then went silent.
      if (!changed && type != EventType.KeyDown && type != EventType.KeyUp) {
        return;
      }

      Log(string.Format("{0,-14} key={1,-12} mods={2}{3}",
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
      try {
        // Diagnostics run BEFORE the disabled check on purpose. They used to sit after it,
        // so once the breaker tripped the logging died with it -- and the console said
        // nothing about why, which looks identical to the hook never having worked.
        SyncDiagnostics();

        if (HasteSettings.DoubleTapShiftDiagnostics) {
          LogEvent(type, key, modifiers);
        }

        if (disabledPermanently) {
          return;
        }

        if (!HasteSettings.DoubleTapShiftEnabled) {
          gesture.Reset();
          return;
        }

        // Repaint and Layout arrive constantly, and IsSuppressed touches half a dozen
        // editor properties. Only pay for that when something could actually move the
        // gesture along.
        var shiftBit = (modifiers & EventModifiers.Shift) != 0;
        var interesting = shiftBit != lastShiftBit ||
          type == EventType.KeyDown || type == EventType.KeyUp ||
          type == EventType.MouseDown || type == EventType.MouseUp ||
          type == EventType.MouseDrag || type == EventType.ScrollWheel;
        lastShiftBit = shiftBit;

        if (!interesting) {
          return;
        }

        gesture.WindowSeconds = HasteSettings.DoubleTapShiftWindowMs / 1000.0;

        var reason = SuppressionReason();

        if (HasteSettings.DoubleTapShiftDiagnostics && reason != null && shiftBit != lastFedShiftBit) {
          Log("suppressed: " + reason);
        }
        lastFedShiftBit = shiftBit;

        if (gesture.Feed(type, key, modifiers, EditorApplication.timeSinceStartup, reason != null)) {
          if (HasteSettings.DoubleTapShiftDiagnostics) {
            Log("FIRED -- opening the palette");
          }
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
    //
    // Returns the REASON rather than a bool, because a silent suppression is
    // indistinguishable from a broken hook: the events still log, the gesture never fires,
    // and nothing says why. That cost a full diagnostic round trip.
    static string SuppressionReason() {
      if (EditorApplication.isPlayingOrWillChangePlaymode) {
        return "play mode";
      }
      if (EditorApplication.isCompiling) {
        return "compiling";
      }
      if (!HasteSettings.Enabled) {
        return "Haste is disabled in preferences";
      }
      if (EditorApplication.isUpdating) {
        return "asset database is updating";
      }
      if (EditorGUIUtility.editingTextField) {
        return "a text field is being edited";
      }

      // EditorGUIUtility.textFieldHasSelection is deliberately NOT checked, though
      // activation-design.md lists it.
      //
      // It is sticky: it reports that some field somewhere still holds a selection, which
      // stays true long after focus has moved on -- including after using Haste's own
      // query field. Measured in a real editor, it latched on and suppressed every
      // subsequent gesture for the rest of the session, which read as "shift-shift stopped
      // working again".
      //
      // Nothing is lost by dropping it. The case it was meant to cover -- Shift extending
      // a selection with the arrow keys -- is Shift plus another key, which the "any other
      // key resets" rule already handles. editingTextField is the signal that actually
      // means "the user is typing right now".
      // A drag in progress: the Shift is snapping something, not asking for a palette.
      if (GUIUtility.hotControl != 0) {
        return "a drag is in progress (hotControl=" + GUIUtility.hotControl + ")";
      }
      // Already open; a second gesture should not stack windows.
      if (HasteSpotlightWindow.IsOpen) {
        return "the palette is already open";
      }
      return null;
    }
  }
}
