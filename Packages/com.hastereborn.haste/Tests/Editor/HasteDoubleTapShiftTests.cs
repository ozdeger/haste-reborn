using NUnit.Framework;
using UnityEngine;

namespace Haste {

  // The double-tap-Shift gesture.
  //
  // Every rule here is a rejection rule, and each one exists because Shift is the most
  // overloaded key in the editor: shift-click range-selects, shift-drag snaps, and
  // Shift+letter is every capital letter. Real input cannot be delivered under -batchmode,
  // so a rule that silently did not apply would only be found by a user whose palette kept
  // popping open mid-sentence. Hence a pure state machine with an injected clock.
  [TestFixture]
  internal class HasteDoubleTapShiftTests {

    HasteDoubleTapShiftGesture gesture;
    double now;

    [SetUp]
    public void SetUp() {
      gesture = new HasteDoubleTapShiftGesture();
      now = 100.0;
    }

    bool Down(KeyCode key = KeyCode.LeftShift, EventModifiers mods = EventModifiers.Shift) {
      return gesture.Feed(EventType.KeyDown, key, mods, now, false);
    }

    bool Up(KeyCode key = KeyCode.LeftShift) {
      return gesture.Feed(EventType.KeyUp, key, EventModifiers.None, now, false);
    }

    void Wait(double seconds) { now += seconds; }

    // A clean double tap. It completes on the second PRESS -- the release afterwards is
    // fed for realism but is not what fires.
    bool Tap(KeyCode key = KeyCode.LeftShift) {
      Down(key); Wait(0.05); Up(key);
      Wait(0.10);
      var fired = Down(key);
      Wait(0.05); Up(key);
      return fired;
    }

    [Test]
    public void ACleanDoubleTapFires() {
      Assert.That(Tap(), Is.True);
      // Either physical Shift, as long as it is the same one twice.
      SetUp();
      Assert.That(Tap(KeyCode.RightShift), Is.True);
    }

    [Test]
    public void ItFiresOnTheSecondPressNotItsRelease() {
      // Measured against 105 real gestures: waiting for the release costs 88ms of median
      // latency -- most of the lag between tapping and being able to type -- and avoids
      // exactly one misfire. Double-click fires on the second press for the same reason.
      Assert.That(Down(), Is.False, "first down");
      Wait(0.05);
      Assert.That(Up(), Is.False, "first up");
      Wait(0.10);
      Assert.That(Down(), Is.True, "the second press is the gesture");
      Wait(0.05);
      Assert.That(Up(), Is.False, "and the release afterwards does nothing");
    }

    [Test]
    public void OnlyTheFirstTapsHoldIsChecked() {
      // The second cannot be: the decision is made before the key comes up. This is the
      // one thing given up by firing on the press, and it is recorded rather than hidden.
      Down(); Wait(0.60);
      Assert.That(Up(), Is.False, "a long FIRST press is still a hold, not a tap");

      SetUp();
      Down(); Wait(0.05); Up();
      Wait(0.10);
      Assert.That(Down(), Is.True, "and the second press fires however long it is then held");
      Wait(0.60);
      Assert.That(Up(), Is.False);
    }

    [Test]
    public void HoldingShiftToTypeCapitalsDoesNotFire() {
      // Key repeat: repeated KeyDown with no release in between. Holding Shift for a run
      // of capitals must never look like two taps.
      //
      // Driving on the modifier BIT makes this structural rather than a rule: the bit is
      // already set, so a repeat is not a transition and nothing happens at all.
      Down(); Wait(0.03);
      Down(); Wait(0.03);
      Down(); Wait(0.03);
      Assert.That(Up(), Is.False);
    }

    // ------------------------------------------------------------ the macOS path

    // What macOS actually delivers: no key event for the Shift at all, just the modifier
    // bits on whatever event comes next. Measured on 6000.3.17f1 -- a bare Shift produced
    // "repaint key=None mods=Shift (was None)" and nothing else.
    bool Bits(EventModifiers mods, EventType type = EventType.Repaint) {
      return gesture.Feed(type, KeyCode.None, mods, now, false);
    }

    [Test]
    public void ItWorksFromModifierBitsAloneWithNoKeyEvents() {
      Bits(EventModifiers.Shift); Wait(0.05);
      Bits(EventModifiers.None);
      Wait(0.10);
      Assert.That(Bits(EventModifiers.Shift), Is.True);
    }

    [Test]
    public void RepeatedEventsCarryingTheSameBitsAreNotTransitions() {
      // Repaint floods. Only a CHANGE means anything, or the palette would open on idle.
      // The repeats have to stay inside MaxTapSeconds, or this stops testing the flood
      // and starts testing the hold limit -- which is what the first draft of this did.
      Bits(EventModifiers.Shift);
      for (var i = 0; i < 10; i++) { Wait(0.003); Bits(EventModifiers.Shift); }
      Bits(EventModifiers.None);
      Wait(0.05);
      Assert.That(Bits(EventModifiers.Shift), Is.True,
        "the flood should have been ignored, leaving one clean tap either side");
    }

    [Test]
    public void PhysicalKeyIdentityIsEnforcedOnlyWhenItIsKnown() {
      // The bits cannot tell LeftShift from RightShift, so the rule is waived when the
      // events carry no keycode -- which on macOS is always. Enforcing it there would
      // disable the gesture outright.
      Bits(EventModifiers.Shift); Wait(0.05); Bits(EventModifiers.None);
      Wait(0.10);
      Assert.That(Bits(EventModifiers.Shift), Is.True);
    }

    [Test]
    public void ATapHeldTooLongIsAHoldNotATap() {
      Down();
      Wait(0.60);
      Assert.That(Up(), Is.False, "a long first press is someone reaching for a capital");
    }

    [Test]
    public void ARealisticallySlowTapStillCounts() {
      // Measured from a real editor log: 77 taps, median 82ms, p90 117ms. The original
      // 120ms limit sat on top of that distribution and silently discarded about one tap
      // in sixteen, which is most of what "it works sometimes" was.
      Down(); Wait(0.117); Up();
      Wait(0.09);
      Assert.That(Down(), Is.True, "a p90 first tap must not be mistaken for a hold");
    }

    [Test]
    public void TwoTapsTooFarApartAreTwoSeparateTaps() {
      Down(); Wait(0.05); Up();
      Wait(1.0);
      Assert.That(Down(), Is.False);
      Wait(0.05); Up();

      // ...and that late tap counts as a fresh first tap, so tapping again completes.
      Wait(0.10);
      Assert.That(Down(), Is.True);
    }

    [Test]
    public void BothTapsMustBeTheSamePhysicalShift() {
      Down(KeyCode.LeftShift); Wait(0.05); Up(KeyCode.LeftShift);
      Wait(0.10);
      Assert.That(Down(KeyCode.RightShift), Is.False);
    }

    [Test]
    public void AnyOtherKeyResets() {
      Down(); Wait(0.05); Up();
      Wait(0.05);
      gesture.Feed(EventType.KeyDown, KeyCode.H, EventModifiers.Shift, now, false);
      Wait(0.05);
      Assert.That(Down(), Is.False, "Shift, H, Shift is typing, not the gesture");
    }

    [Test]
    public void TypingACapitalLetterDoesNotFire() {
      // The realistic false positive, in the exact shape the editor delivers it: Shift
      // goes down, a letter is typed while it is held, Shift comes up.
      Bits(EventModifiers.Shift); Wait(0.02);
      gesture.Feed(EventType.KeyDown, KeyCode.H, EventModifiers.Shift, now, false);
      Wait(0.02);
      gesture.Feed(EventType.KeyUp, KeyCode.H, EventModifiers.Shift, now, false);
      Wait(0.02);
      Bits(EventModifiers.None);
      Wait(0.05);
      Assert.That(Bits(EventModifiers.Shift), Is.False,
        "the letter reset it, so this press starts a fresh first tap");
    }

    [Test]
    public void AnotherModifierMeansItWasAChord() {
      // Cmd+Shift or Ctrl+Shift is someone starting a shortcut, not tapping.
      Down(mods: EventModifiers.Shift | EventModifiers.Command);
      Wait(0.05); Up();
      Wait(0.10);
      Assert.That(Down(), Is.False);
    }

    [Test]
    public void HoldingCommandThenShiftIsAChordToo() {
      // The bits arrive one at a time, so this is the shape a real Cmd+Shift makes.
      Bits(EventModifiers.Command); Wait(0.05);
      Bits(EventModifiers.Command | EventModifiers.Shift); Wait(0.05);
      Bits(EventModifiers.Command);
      Wait(0.05);
      Assert.That(Bits(EventModifiers.Command | EventModifiers.Shift), Is.False);
    }

    [Test]
    public void IncidentalModifierBitsAreIgnored() {
      // CapsLock, NumLock and the fn key get set by the OS and say nothing about intent.
      var noisy = EventModifiers.Shift | EventModifiers.CapsLock |
                  EventModifiers.Numeric | EventModifiers.FunctionKey;
      Down(mods: noisy); Wait(0.05); Up();
      Wait(0.10);
      Assert.That(Down(mods: noisy), Is.True);
    }

    [Test]
    public void MouseActivityResets() {
      foreach (var mouse in new[] { EventType.MouseDown, EventType.MouseUp,
                                    EventType.MouseDrag, EventType.ScrollWheel }) {
        SetUp();
        Down(); Wait(0.05); Up();
        gesture.Feed(mouse, KeyCode.None, EventModifiers.Shift, now, false);
        Wait(0.05);
        Assert.That(Down(), Is.False, mouse + " should have reset the gesture");
      }
    }

    [Test]
    public void SuppressionResetsRatherThanMerelyIgnoring() {
      // Otherwise a gesture could span the boundary: one tap before a text field takes
      // focus and one after.
      Down(); Wait(0.05); Up();
      gesture.Feed(EventType.KeyDown, KeyCode.LeftShift, EventModifiers.Shift, now, true);
      Wait(0.05);
      Assert.That(Down(), Is.False);
    }

    [Test]
    public void DeliberateRepeatedUseDoesNotTripTheBreaker() {
      // The regression that mattered most. At 3-fires-in-10s the breaker treated somebody
      // testing the gesture as a false positive and disabled it for the session -- which
      // is exactly what "it worked a few times then stopped" was.
      for (var i = 0; i < 5; i++) {
        Assert.That(Tap(), Is.True, "ordinary repeated use, fire " + (i + 1));
        Wait(0.5);
      }
      Assert.That(gesture.TrippedBreaker, Is.False);
    }

    [Test]
    public void TheBreakerStillStopsARunawayGesture() {
      // A genuine storm: firing far faster than a person can tap, which is what it would
      // look like if the rules let ordinary typing through.
      for (var i = 0; i < 6; i++) {
        Assert.That(Tap(), Is.True, "fire " + (i + 1) + " should be allowed");
      }

      Assert.That(Tap(), Is.False, "the seventh fire inside two seconds trips the breaker");
      Assert.That(gesture.TrippedBreaker, Is.True);

      // And it stays tripped, however long you wait, until it is explicitly cleared.
      Wait(60.0);
      Assert.That(Tap(), Is.False);

      gesture.ClearBreaker();
      Assert.That(Tap(), Is.True, "Reset double-tap state must actually recover it");
    }

    [Test]
    public void TheWindowIsConfigurable() {
      gesture.WindowSeconds = 0.5;
      Down(); Wait(0.05); Up();
      Wait(0.40);
      Assert.That(Down(), Is.True, "a widened window should accept a slower tap");
    }
  }
}
