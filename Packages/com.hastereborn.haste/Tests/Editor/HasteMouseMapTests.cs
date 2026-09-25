using NUnit.Framework;

namespace Haste {

  // The click map, which is the whole of what a mouse press on a row decides.
  //
  // Worth testing precisely because the handler around it cannot be: UI Toolkit does not
  // run headlessly, so a wrong branch there compiles and passes everything.
  [TestFixture]
  internal class HasteMouseMapTests {

    static HasteClickIntent Resolve(
      int clickCount, bool actionKey = false, bool hasObject = true, bool multiSelecting = false) {
      return HasteMouseMap.Resolve(clickCount, actionKey, hasObject, multiSelecting);
    }

    [Test]
    public void ASingleClickRevealsTheRow() {
      Assert.That(Resolve(1), Is.EqualTo(HasteClickIntent.Reveal));
    }

    [Test]
    public void ADoubleClickOpensIt() {
      Assert.That(Resolve(2), Is.EqualTo(HasteClickIntent.Open));
    }

    [Test]
    public void AThirdClickStillOpensRatherThanFallingBack() {
      // Click counts keep climbing while the clicks keep coming, so a triple click must
      // not land back on Reveal -- which a clickCount == 2 test would have allowed.
      Assert.That(Resolve(3), Is.EqualTo(HasteClickIntent.Open));
      Assert.That(Resolve(4), Is.EqualTo(HasteClickIntent.Open));
    }

    [Test]
    public void ADoubleClickOpensARowWithNothingToReveal() {
      // A menu item has no object. Open falls back to Action on those, so double-clicking
      // one runs it, exactly as Enter does.
      Assert.That(Resolve(2, hasObject: false), Is.EqualTo(HasteClickIntent.Open));
    }

    [Test]
    public void ASingleClickOnARowWithNothingToRevealOnlyMovesTheCursor() {
      // The regression this guards: revealing a menu item means running it, and "Delete"
      // is one click away from anything else in the same list. A single click must never
      // be the thing that runs a command.
      Assert.That(Resolve(1, hasObject: false), Is.EqualTo(HasteClickIntent.Highlight));
    }

    [Test]
    public void TheActionKeyTogglesTheMultiSelectionWhateverTheClickCount() {
      Assert.That(Resolve(1, actionKey: true), Is.EqualTo(HasteClickIntent.ToggleMultiSelect));
      Assert.That(Resolve(2, actionKey: true), Is.EqualTo(HasteClickIntent.ToggleMultiSelect));
    }

    [Test]
    public void TheActionKeyWinsOverARowWithNothingToReveal() {
      Assert.That(Resolve(1, actionKey: true, hasObject: false),
        Is.EqualTo(HasteClickIntent.ToggleMultiSelect));
    }

    [Test]
    public void ASingleClickDuringAMultiSelectionDoesNotReveal() {
      // Reveal assigns Selection.objects a single row, which would throw away a
      // multi-selection the user is still assembling.
      Assert.That(Resolve(1, multiSelecting: true), Is.EqualTo(HasteClickIntent.Highlight));
    }

    [Test]
    public void ADoubleClickDuringAMultiSelectionStillCommits() {
      // Act short-circuits on a non-empty multi-selection and confirms it, so Open is the
      // right intent to hand it: the double click ends the multi-select rather than
      // opening one row out of it.
      Assert.That(Resolve(2, multiSelecting: true), Is.EqualTo(HasteClickIntent.Open));
    }
  }
}
