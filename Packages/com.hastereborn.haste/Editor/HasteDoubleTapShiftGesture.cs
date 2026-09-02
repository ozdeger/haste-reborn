using UnityEngine;

namespace Haste {

  // Recognises "tap Shift twice" from a raw event stream.
  //
  // Driven by TRANSITIONS OF THE SHIFT MODIFIER BIT, not by KeyDown/KeyUp of a Shift key.
  // That is not a preference, it is the only thing that works: measured on macOS
  // 6000.3.17f1, pressing Shift alone produces no key event at all -- it is an NSEvent
  // flagsChanged -- and surfaces only as the modifier bits on whatever event comes next:
  //
  //     [Haste] modifierKeysChanged
  //     [Haste] repaint  key=None  mods=Shift  (was None)
  //
  // Reading the bit works on every platform, since a real Shift KeyDown carries the bit
  // too. It costs one invariant: the bits cannot tell LeftShift from RightShift, so "both
  // taps must be the same physical key" is enforced only when the events happen to carry a
  // keycode, and waived when they do not.
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
    //
    // 250ms, not the 120ms activation-design.md guessed at. Measured from 77 real taps in
    // an editor log: median 82ms, p90 117ms, and 6% of genuine taps ran past 120ms -- the
    // old limit sat directly on top of the distribution it was meant to be clear of, so
    // roughly one tap in sixteen was silently discarded as "a hold". Actual holds in the
    // same log ran 1300-1600ms, so there is a wide gap to sit in.
    public double MaxTapSeconds = 0.25;

    // Runaway breaker: the net for whatever false-positive class was not anticipated.
    //
    // 6 fires in 2 seconds, not the 3-in-10 activation-design.md specified. That figure
    // was picked without data and it is not a false-positive detector -- it is a
    // heavy-use detector. In a real editor log it tripped twice, both times because
    // someone was TESTING the gesture: opening the palette four times in ten seconds is
    // ordinary use, and disabling the feature for the rest of the session was the single
    // biggest cause of "it works, then it stops".
    //
    // A human double tap takes roughly 250-400ms end to end, so seven of them inside two
    // seconds is not something deliberate use reaches. A genuine storm -- the gesture
    // firing on ordinary typing -- clears it easily.
    public int MaxFiresPerWindow = 6;
    public double BreakerWindowSeconds = 2.0;

    Phase phase;

    // The last observed state of the Shift bit. Transitions of this are the gesture.
    bool shiftHeld;

    // KeyCode.None when the transition arrived on an event that carries no key -- which on
    // macOS is every time.
    KeyCode firstTapKey, secondTapKey;

    double firstDownAt, firstUpAt, secondDownAt;

    int firesInWindow;
    double breakerWindowStartedAt;

    public bool TrippedBreaker { get; private set; }

    // Clears a tripped breaker. Without this a false positive disables the gesture for
    // the rest of the domain with no way back.
    public void ClearBreaker() {
      TrippedBreaker = false;
      firesInWindow = 0;
      breakerWindowStartedAt = 0.0;
    }

    public void Reset() {
      phase = Phase.Idle;
      firstTapKey = KeyCode.None;
      secondTapKey = KeyCode.None;
    }

    // Same physical key, as far as can be told. Two known keycodes must match; an unknown
    // one on either side is accepted, because macOS never reports which Shift it was.
    static bool SameKey(KeyCode a, KeyCode b) {
      return a == KeyCode.None || b == KeyCode.None || a == b;
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
      var nowHeld = (modifiers & EventModifiers.Shift) != 0;

      if (suppressed || TrippedBreaker) {
        shiftHeld = nowHeld;
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
          shiftHeld = nowHeld;
          Reset();
          return false;

        case EventType.KeyDown:
          // Any other key resets. This is why the detector has to be the PRE-consumption
          // hook: a focused text field eats the letter between two Shift taps but passes
          // the Shifts through, so on a post-consumption hook this rule would be a no-op
          // exactly while someone types CamelCase.
          if (!IsShift(key)) {
            shiftHeld = nowHeld;
            Reset();
            return false;
          }
          break;
      }

      if (nowHeld == shiftHeld) {
        return false;
      }
      shiftHeld = nowHeld;

      return nowHeld
        ? OnShiftDown(IsShift(key) ? key : KeyCode.None, modifiers, time)
        : OnShiftUp(IsShift(key) ? key : KeyCode.None, time);
    }

    bool OnShiftDown(KeyCode key, EventModifiers modifiers, double time) {
      // Shift with anything else is someone starting a chord, not tapping.
      if (!OnlyShift(modifiers)) {
        Reset();
        return false;
      }

      if (phase == Phase.FirstUp) {
        // Too slow, or the other Shift key, is not this gesture -- but it is a perfectly
        // good start for the next one.
        if (SameKey(firstTapKey, key) && time - firstUpAt <= WindowSeconds) {
          phase = Phase.SecondDown;
          secondTapKey = key;
          secondDownAt = time;
          return false;
        }
      }

      StartFirstTap(key, time);
      return false;
    }

    bool OnShiftUp(KeyCode key, double time) {
      if (phase == Phase.FirstDown) {
        // A press held longer than a tap was a hold: reaching for a capital, or
        // shift-dragging in the Scene view.
        if (!SameKey(firstTapKey, key) || time - firstDownAt > MaxTapSeconds) {
          Reset();
          return false;
        }
        phase = Phase.FirstUp;
        firstUpAt = time;
        return false;
      }

      if (phase == Phase.SecondDown) {
        var held = time - secondDownAt;
        var matched = SameKey(secondTapKey, key);
        Reset();

        if (!matched || held > MaxTapSeconds) {
          return false;
        }
        return RecordFire(time);
      }

      Reset();
      return false;
    }

    void StartFirstTap(KeyCode key, double time) {
      phase = Phase.FirstDown;
      firstTapKey = key;
      secondTapKey = KeyCode.None;
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
