using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Haste {

  // The recency list.
  //
  // Exercised through the static Record/Dedupe rather than the ScriptableSingleton,
  // because the singleton is the developer's real recency file and a test has no business
  // rewriting it -- and because the bug these cover was pure list arithmetic.
  [TestFixture]
  internal class HasteRecommendationTests {

    static HasteItem Asset(string name, float score) {
      var item = new HasteItem("Assets/Prefabs/UI/" + name + ".prefab", 0, HasteProjectSource.NAME);
      item.userScore = score;
      return item;
    }

    [Test]
    public void RecordingAnItemAlreadyInTheListDoesNotDuplicateIt() {
      // The reported symptom: the same prefab twice on the launch screen.
      //
      // 0.11 decays to 0.099 and falls under the 0.1 threshold, so exactly one entry is
      // removed. The old code had already read X's position as 2; after the removal slot
      // 2 held C, so C was overwritten with X and the original X stayed put.
      var recent = new List<HasteItem> {
        Asset("Dying", 0.11f), Asset("B", 0.5f), Asset("X", 0.5f), Asset("C", 0.5f),
      };

      Assert.That(HasteRecommendations.Record(recent, Asset("X", 0.0f)), Is.True);

      var paths = recent.Select(i => i.path).ToArray();
      Assert.That(paths.Count(p => p.Contains("/X.")), Is.EqualTo(1), "X must appear once");
      Assert.That(paths.Any(p => p.Contains("/C.")), Is.True, "C must not be overwritten");
      Assert.That(paths.Any(p => p.Contains("/Dying.")), Is.False, "the decayed entry goes");
      Assert.That(recent.Count, Is.EqualTo(3));
    }

    [Test]
    public void RecordingDoesNotThrowWhenSeveralEntriesDecayAtOnce() {
      // The other face of the same defect. Two entries die, the list shrinks to one, and
      // the position read beforehand -- 2 -- is now past the end. That was the
      // ArgumentOutOfRangeException out of HasteSpotlightWindow.Act.
      var recent = new List<HasteItem> {
        Asset("DyingA", 0.11f), Asset("DyingB", 0.11f), Asset("X", 0.5f),
      };

      Assert.That(() => HasteRecommendations.Record(recent, Asset("X", 0.0f)), Throws.Nothing);
      Assert.That(recent.Count, Is.EqualTo(1));
      Assert.That(recent[0].path, Does.Contain("/X."));
      Assert.That(recent[0].userScore, Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void RePickingTheTopItemChangesNothing() {
      // Guards the early return: without it, picking the same row twice would decay the
      // whole list for no reason.
      var top = Asset("X", 1.0f);
      var recent = new List<HasteItem> { Asset("B", 0.5f), top };

      Assert.That(HasteRecommendations.Record(recent, top), Is.False);
      Assert.That(recent.Count, Is.EqualTo(2));
      Assert.That(recent[0].userScore, Is.EqualTo(0.5f).Within(0.001f), "nothing decayed");
    }

    [Test]
    public void RecordingPromotesTheItemAndDecaysTheRest() {
      var recent = new List<HasteItem> { Asset("B", 0.8f) };

      HasteRecommendations.Record(recent, Asset("X", 0.0f));

      var b = recent.Single(i => i.path.Contains("/B."));
      var x = recent.Single(i => i.path.Contains("/X."));
      Assert.That(b.userScore, Is.EqualTo(0.72f).Within(0.001f));
      Assert.That(x.userScore, Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void DedupeHealsAListTheOldCodeAlreadyWroteTwice() {
      // A recency file written by the buggy version keeps its duplicates until each item
      // happens to be picked again, so they are collapsed on load instead.
      var recent = new List<HasteItem> {
        Asset("X", 0.4f), Asset("B", 0.5f), Asset("X", 0.9f), Asset("C", 0.3f),
      };

      Assert.That(HasteRecommendations.Dedupe(recent), Is.True);
      Assert.That(recent.Count, Is.EqualTo(3));

      var x = recent.Single(i => i.path.Contains("/X."));
      Assert.That(x.userScore, Is.EqualTo(0.9f).Within(0.001f),
        "the higher score survives, since Get orders by it");

      // And a clean list is left exactly as it is.
      Assert.That(HasteRecommendations.Dedupe(recent), Is.False);
    }

    [Test]
    public void TheNullCasesAreNotCrashes() {
      Assert.That(HasteRecommendations.Record(null, Asset("X", 0f)), Is.False);
      Assert.That(HasteRecommendations.Record(new List<HasteItem>(), null), Is.False);
      Assert.That(HasteRecommendations.Dedupe(null), Is.False);
    }
  }
}
