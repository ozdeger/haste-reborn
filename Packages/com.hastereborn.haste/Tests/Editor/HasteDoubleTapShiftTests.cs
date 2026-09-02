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

    // A clean double tap: down, up, down, up, all inside the windows.
    bool Tap(KeyCode key = KeyCode.LeftShift) {
      Down(key); Wait(0.05); Up(key);
      Wait(0.10);
      Down(key); Wait(0.05);
      return Up(key);
    }

    [Test]
    public void ACleanDoubleTapFires() {
      Assert.That(Tap(), Is.True);
      // Either physical Shift, as long as it is the same one twice.
      SetUp();
      Assert.That(Tap(KeyCode.RightShift), Is.True);
    }

    [Test]
    public void ItFiresOnTheSecondKeyUpAndNotBefore() {
      Assert.That(Down(), Is.False, "first down");
      Wait(0.05);
      Assert.That(Up(), Is.False, "first up");
      Wait(0.10);
      Assert.That(Down(), Is.False, "second down must not fire -- the release is the gesture");
      Wait(0.05);
      Assert.That(Up(), Is.True);
    }

    [Test]
    public void HoldingShiftToTypeCapitalsDoesNotFire() {
      // Key repeat: repeated KeyDown with no KeyUp in between. This is what holding Shift
      // to type a run of capitals looks like, and it must never be mistaken for two taps.
      Down(); Wait(0.03);
      Down(); Wait(0.03);
      Down(); Wait(0.03);
      Assert.That(Up(), Is.False);
    }

    [Test]
    public void ATapHeldTooLongIsAHoldNotATap() {
      Down();
      Wait(0.30);
      Assert.That(Up(), Is.False, "a long first press is someone reaching for a capital");

      SetUp();
      Down(); Wait(0.05); Up();
      Wait(0.10);
      Down(); Wait(0.30);
      Assert.That(Up(), Is.False, "a long second press is a hold too");
    }

    [Test]
    public void TwoTapsTooFarApartAreTwoSeparateTaps() {
      Down(); Wait(0.05); Up();
      Wait(1.0);
      Down(); Wait(0.05);
      Assert.That(Up(), Is.False);

      // ...and that late tap counts as a fresh first tap, so tapping again completes.
      Wait(0.10);
      Down(); Wait(0.05);
      Assert.That(Up(), Is.True);
    }

    [Test]
    public void BothTapsMustBeTheSamePhysicalShift() {
      Down(KeyCode.LeftShift); Wait(0.05); Up(KeyCode.LeftShift);
      Wait(0.10);
      Down(KeyCode.RightShift); Wait(0.05);
      Assert.That(Up(KeyCode.RightShift), Is.False);
    }

    [Test]
    public void AnyOtherKeyResets() {
      Down(); Wait(0.05); Up();
      Wait(0.05);
      gesture.Feed(EventType.KeyDown, KeyCode.H, EventModifiers.Shift, now, false);
      Wait(0.05);
      Down(); Wait(0.05);
      Assert.That(Up(), Is.False, "Shift, H, Shift is typing, not the gesture");
    }

    [Test]
    public void AnotherModifierMeansItWasAChord() {
      // Cmd+Shift or Ctrl+Shift is someone starting a shortcut, not tapping.
      Down(mods: EventModifiers.Shift | EventModifiers.Command);
      Wait(0.05); Up();
      Wait(0.10);
      Down(); Wait(0.05);
      Assert.That(Up(), Is.False);
    }

    [Test]
    public void IncidentalModifierBitsAreIgnored() {
      // CapsLock, NumLock and the fn key get set by the OS and say nothing about intent.
      var noisy = EventModifiers.Shift | EventModifiers.CapsLock |
                  EventModifiers.Numeric | EventModifiers.FunctionKey;
      Down(mods: noisy); Wait(0.05); Up();
      Wait(0.10);
      Down(mods: noisy); Wait(0.05);
      Assert.That(Up(), Is.True);
    }

    [Test]
    public void MouseActivityResets() {
      foreach (var mouse in new[] { EventType.MouseDown, EventType.MouseUp,
                                    EventType.MouseDrag, EventType.ScrollWheel }) {
        SetUp();
        Down(); Wait(0.05); Up();
        gesture.Feed(mouse, KeyCode.None, EventModifiers.Shift, now, false);
        Wait(0.05);
        Down(); Wait(0.05);
        Assert.That(Up(), Is.False, mouse + " should have reset the gesture");
      }
    }

    [Test]
    public void SuppressionResetsRatherThanMerelyIgnoring() {
      // Otherwise a gesture could span the boundary: one tap before a text field takes
      // focus and one after.
      Down(); Wait(0.05); Up();
      gesture.Feed(EventType.KeyDown, KeyCode.LeftShift, EventModifiers.Shift, now, true);
      Wait(0.05);
      Down(); Wait(0.05);
      Assert.That(Up(), Is.False);
    }

    [Test]
    public void TheBreakerStopsARunawayGesture() {
      // The net for whatever false-positive class was not anticipated: repeated fires in
      // a few seconds mean the rules are wrong, and firing is worse than not.
      for (var i = 0; i < 3; i++) {
        Assert.That(Tap(), Is.True, "fire " + i + " should be allowed");
        Wait(0.2);
      }

      Assert.That(Tap(), Is.False, "the fourth fire within the window trips the breaker");
      Assert.That(gesture.TrippedBreaker, Is.True);

      // And it stays tripped, however long you wait.
      Wait(60.0);
      Assert.That(Tap(), Is.False);
    }

    [Test]
    public void TheWindowIsConfigurable() {
      gesture.WindowSeconds = 0.5;
      Down(); Wait(0.05); Up();
      Wait(0.40);
      Down(); Wait(0.05);
      Assert.That(Up(), Is.True, "a widened window should accept a slower tap");
    }
  }
}
