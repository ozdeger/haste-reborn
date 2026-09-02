using NUnit.Framework;
using UnityEngine;

namespace Haste {

  // The palette's keyboard map.
  //
  // These exist because of a real failure: the right arrow shipped doing nothing. An edit
  // that should have added the binding was lost, and nothing caught it -- the code
  // compiled, every test passed, and UI Toolkit cannot be driven headlessly, so the only
  // way to find it was to press the key. Pulling the mapping out of the window makes it
  // ordinary testable logic.
  [TestFixture]
  internal class HasteKeyMapTests {

    static HasteKeyIntent Results(KeyCode key, bool actionKey = false, bool shift = false,
                                  bool hasScope = false, bool queryIsEmpty = false) {
      return HasteKeyMap.Resolve(key, actionKey, shift, false, false, hasScope, queryIsEmpty, true);
    }

    static HasteKeyIntent Actions(KeyCode key, bool actionKey = false, bool shift = false,
                                  bool atRoot = true) {
      return HasteKeyMap.Resolve(key, actionKey, shift, false, true, false, false, atRoot);
    }

    [Test]
    public void TheActionsPaneNestsWithTheArrowKeys() {
      // A context menu has submenus, so the pane has levels. Right goes deeper, mirroring
      // the key that opened it.
      Assert.That(Actions(KeyCode.RightArrow), Is.EqualTo(HasteKeyIntent.EnterSubmenu));
      Assert.That(Actions(KeyCode.RightArrow, atRoot: false), Is.EqualTo(HasteKeyIntent.EnterSubmenu));

      // Left retraces, and only closes the pane once there is nothing left to retrace.
      Assert.That(Actions(KeyCode.LeftArrow, atRoot: false), Is.EqualTo(HasteKeyIntent.LeaveSubmenu));
      Assert.That(Actions(KeyCode.LeftArrow), Is.EqualTo(HasteKeyIntent.HideActions));

      // Escape closes the palette outright from any depth, rather than unwinding. Going
      // back is the left arrow's job and only the left arrow's.
      Assert.That(Actions(KeyCode.Escape), Is.EqualTo(HasteKeyIntent.Dismiss));
      Assert.That(Actions(KeyCode.Escape, atRoot: false), Is.EqualTo(HasteKeyIntent.Dismiss));

      // Enter still resolves to RunAction at any depth -- the window turns it into a
      // descend when the highlighted row is a submenu, because only it knows that.
      Assert.That(Actions(KeyCode.Return, atRoot: false), Is.EqualTo(HasteKeyIntent.RunAction));
    }

    [Test]
    public void RightArrowAndCommandKOpenTheActionsPane() {
      // The binding that was missing.
      Assert.That(Results(KeyCode.RightArrow), Is.EqualTo(HasteKeyIntent.ShowActions));
      Assert.That(Results(KeyCode.K, actionKey: true), Is.EqualTo(HasteKeyIntent.ShowActions));

      // A bare "k" is a character to type, not a command.
      Assert.That(Results(KeyCode.K), Is.EqualTo(HasteKeyIntent.None));
    }

    [Test]
    public void EnterRevealsAndShiftEnterOpens() {
      Assert.That(Results(KeyCode.Return), Is.EqualTo(HasteKeyIntent.Reveal));
      Assert.That(Results(KeyCode.KeypadEnter), Is.EqualTo(HasteKeyIntent.Reveal));
      Assert.That(Results(KeyCode.Return, shift: true), Is.EqualTo(HasteKeyIntent.Open));
      Assert.That(Results(KeyCode.Return, actionKey: true), Is.EqualTo(HasteKeyIntent.ToggleMultiSelect));

      // The multi-select chord wins over Shift, so Cmd+Shift+Enter still adds to the set
      // rather than opening one thing.
      Assert.That(Results(KeyCode.Return, actionKey: true, shift: true),
        Is.EqualTo(HasteKeyIntent.ToggleMultiSelect));
    }

    [Test]
    public void ArrowsAndPagingMoveTheHighlight() {
      Assert.That(Results(KeyCode.UpArrow), Is.EqualTo(HasteKeyIntent.MoveUp));
      Assert.That(Results(KeyCode.DownArrow), Is.EqualTo(HasteKeyIntent.MoveDown));
      Assert.That(Results(KeyCode.Home), Is.EqualTo(HasteKeyIntent.MoveHome));
      Assert.That(Results(KeyCode.End), Is.EqualTo(HasteKeyIntent.MoveEnd));
      Assert.That(Results(KeyCode.PageUp), Is.EqualTo(HasteKeyIntent.MovePageUp));
      Assert.That(Results(KeyCode.PageDown), Is.EqualTo(HasteKeyIntent.MovePageDown));
      Assert.That(Results(KeyCode.Escape), Is.EqualTo(HasteKeyIntent.Dismiss));
    }

    [Test]
    public void BackspaceOnlyClearsTheScopeOnceThereIsNothingLeftToDelete() {
      Assert.That(Results(KeyCode.Backspace, hasScope: true, queryIsEmpty: true),
        Is.EqualTo(HasteKeyIntent.ClearScope));

      // Otherwise it must fall through to the text field, or the query cannot be edited.
      Assert.That(Results(KeyCode.Backspace, hasScope: true, queryIsEmpty: false),
        Is.EqualTo(HasteKeyIntent.None));
      Assert.That(Results(KeyCode.Backspace, hasScope: false, queryIsEmpty: true),
        Is.EqualTo(HasteKeyIntent.None));
    }

    [Test]
    public void TheActionsPaneOwnsTheKeyboardWhileItIsOpen() {
      Assert.That(Actions(KeyCode.UpArrow), Is.EqualTo(HasteKeyIntent.ActionUp));
      Assert.That(Actions(KeyCode.DownArrow), Is.EqualTo(HasteKeyIntent.ActionDown));
      Assert.That(Actions(KeyCode.LeftArrow), Is.EqualTo(HasteKeyIntent.HideActions));
      Assert.That(Actions(KeyCode.Return), Is.EqualTo(HasteKeyIntent.RunAction));

      // Escape means one thing everywhere in the palette: put it away. It is deliberately
      // NOT a back key -- that is the left arrow's job, and Escape that unwinds one level
      // takes more presses the deeper you are.
      Assert.That(Actions(KeyCode.Escape), Is.EqualTo(HasteKeyIntent.Dismiss));
      Assert.That(Results(KeyCode.Escape), Is.EqualTo(HasteKeyIntent.Dismiss));

      // Nothing from the results list leaks through. The right arrow is NOT in this
      // list any more -- it descends into a submenu; see the nesting test.
      Assert.That(Actions(KeyCode.Home), Is.EqualTo(HasteKeyIntent.None));
      Assert.That(Actions(KeyCode.Backspace), Is.EqualTo(HasteKeyIntent.None));
    }

    [Test]
    public void OrdinaryTypingIsLeftAlone() {
      // Anything the map does not claim must reach the text field untouched, or the
      // palette cannot be typed into at all.
      foreach (var key in new[] { KeyCode.A, KeyCode.Z, KeyCode.Space, KeyCode.Period,
                                  KeyCode.Alpha1, KeyCode.Slash, KeyCode.Greater }) {
        Assert.That(Results(key), Is.EqualTo(HasteKeyIntent.None), key + " was swallowed");
      }
    }
  }
}
