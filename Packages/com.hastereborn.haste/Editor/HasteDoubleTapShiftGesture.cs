using UnityEngine;

namespace Haste {

  // Recognises "tap Shift twice" from a raw event stream.
  //
  // Pure and clock-injected on purpose. Real keyboard input cannot be delivered under
  // -batchmode, so every invariant in Documentation~/activation-design.md would otherwise
  // be unverifiable -- and Shift is the most overloaded key in the editor, so a
  // false-positive rule that silently does not apply is worse than no rule.
  //
  // Everything here is a rejection rule. The gesture fires only when nothing objected.
  public class HasteDoubleTapShiftGesture {

    enum Phase { Idle, FirstDown, FirstUp, SecondDown }

    // Bits the OS sets incidentally and that say nothing about intent.
    const EventModifiers Incidental =
      EventModifiers.Numeric | EventModifiers.CapsLock | EventModifiers.FunctionKey;

    // The only user-tunable value. Typing rhythm varies, and the gesture cannot appear in
    // Edit > Shortcuts, so Haste's preferences are the only place to widen or escape it.
    public double WindowSeconds = 0.25;

    // A press held longer than this was a hold, not a tap -- someone reaching for a
    // capital letter, or shift-dragging in the Scene view.
    public double MaxTapSeconds = 0.12;

    // Runaway breaker. Whatever false-positive class was not anticipated, this is the net:
    // more than a few fires in quick succession means the rules are wrong, and firing is
    // worse than not.
    public int MaxFiresPerWindow = 3;
    public double BreakerWindowSeconds = 10.0;

    Phase phase;
    KeyCode tapKey;
    double firstDownAt, firstUpAt, secondDownAt;

    int firesInWindow;
    double breakerWindowStartedAt;

    public bool TrippedBreaker { get; private set; }

    public void Reset() {
      phase = Phase.Idle;
      tapKey = KeyCode.None;
    }

    public static bool IsShift(KeyCode key) {
      return key == KeyCode.LeftShift || key == KeyCode.RightShift;
    }

    // Shift and nothing else. A KeyUp may or may not still carry the Shift bit depending
    // on the platform, so None is allowed too.
    public static bool OnlyShift(EventModifiers modifiers) {
      var significant = modifiers & ~Incidental;
      return significant == EventModifiers.None || significant == EventModifiers.Shift;
    }

    // Returns true exactly once, on the second KeyUp, when the gesture completed.
    //
    // `suppressed` folds in everything the caller knows and this cannot: a text field
    // being edited, play mode, indexing. It resets rather than merely ignoring, so a
    // gesture cannot span the boundary.
    public bool Feed(EventType type, KeyCode key, EventModifiers modifiers, double time, bool suppressed) {
      if (suppressed || TrippedBreaker) {
        Reset();
        return false;
      }

      switch (type) {
        // Any mouse activity means the Shift was a modifier for something else.
        case EventType.MouseDown:
        case EventType.MouseUp:
        case EventType.MouseDrag:
        case EventType.ScrollWheel:
        case EventType.DragUpdated:
        case EventType.DragPerform:
          Reset();
          return false;

        case EventType.KeyDown:
          return OnKeyDown(key, modifiers, time);

        case EventType.KeyUp:
          return OnKeyUp(key, time);
      }

      return false;
    }

    bool OnKeyDown(KeyCode key, EventModifiers modifiers, double time) {
      // Any other key resets. This is why the detector has to be the PRE-consumption hook:
      // a focused text field eats the letter between two Shifts but passes the Shifts
      // through, so on a post-consumption hook this rule would be a no-op precisely while
      // someone types CamelCase.
      if (!IsShift(key) || !OnlyShift(modifiers)) {
        Reset();
        return false;
      }

      switch (phase) {
        case Phase.Idle:
          StartFirstTap(key, time);
          return false;

        case Phase.FirstDown:
          // A second KeyDown with no KeyUp between is key repeat from holding Shift.
          Reset();
          return false;

        case Phase.FirstUp:
          // A different Shift key, or too slow, is not this gesture -- but it is a
          // perfectly good start for the next one.
          if (key != tapKey || time - firstUpAt > WindowSeconds) {
            StartFirstTap(key, time);
            return false;
          }
          phase = Phase.SecondDown;
          secondDownAt = time;
          return false;
      }

      Reset();
      return false;
    }

    bool OnKeyUp(KeyCode key, double time) {
      if (!IsShift(key) || key != tapKey) {
        Reset();
        return false;
      }

      if (phase == Phase.FirstDown) {
        if (time - firstDownAt > MaxTapSeconds) {
          Reset();
          return false;
        }
        phase = Phase.FirstUp;
        firstUpAt = time;
        return false;
      }

      if (phase == Phase.SecondDown) {
        Reset();
        if (time - secondDownAt > MaxTapSeconds) {
          return false;
        }
        return RecordFire(time);
      }

      Reset();
      return false;
    }

    void StartFirstTap(KeyCode key, double time) {
      phase = Phase.FirstDown;
      tapKey = key;
      firstDownAt = time;
    }

    bool RecordFire(double time) {
      if (time - breakerWindowStartedAt > BreakerWindowSeconds) {
        breakerWindowStartedAt = time;
        firesInWindow = 0;
      }

      firesInWindow++;

      if (firesInWindow > MaxFiresPerWindow) {
        TrippedBreaker = true;
        return false;
      }

      return true;
    }
  }
}
