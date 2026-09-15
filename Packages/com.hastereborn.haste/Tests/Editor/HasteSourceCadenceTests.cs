using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Haste {

  // Pins when a source is allowed to crawl.
  //
  // The bug these exist for: EditorApplication.hierarchyChanged restarted a full walk of
  // the scene, and the walk could not finish before the next event arrived. Measured on
  // 6000.3.17f1 with a 100k-GameObject scene and the palette closed, 8 seconds of
  // ordinary hierarchy edits started 46 crawls, completed none, and held 98% of the main
  // thread. The fix is not a shorter walk -- it is that a change event no longer starts
  // one.
  //
  // HasteWatcherManager keeps its registry in statics, so every fixture here registers
  // under a name of its own and removes it again.
  [TestFixture]
  internal class HasteSourceCadenceTests {

    HasteWatcherManager watchers;
    string sourceName;
    int crawls;

    [SetUp]
    public void SetUp() {
      watchers = new HasteWatcherManager();
      sourceName = "HasteCadenceFixture:" + System.Guid.NewGuid().ToString("N");
      crawls = 0;
    }

    [TearDown]
    public void TearDown() {
      watchers.RemoveSource(sourceName);
    }

    // A source whose every enumeration is counted, so "did it crawl?" is a fact rather
    // than an inference from the index.
    IEnumerable<HasteItem> CountedSource() {
      crawls++;
      yield return new HasteItem("Fixture/One", 0, sourceName);
      yield return new HasteItem("Fixture/Two", 0, sourceName);
    }

    // A source that is guaranteed to still be mid-walk after one tick: HasteWatcher
    // yields as soon as it has spent MAX_ITER_TIME, so overrunning it once puts the
    // crawl into exactly the state Refresh must not restart.
    IEnumerable<HasteItem> SlowSource() {
      crawls++;
      System.Threading.Thread.Sleep((int)(Haste.MAX_ITER_TIME * 1000) + 8);
      yield return new HasteItem("Fixture/One", 0, sourceName);
      yield return new HasteItem("Fixture/Two", 0, sourceName);
    }

    // Ticks the scheduler the way Haste.Update does across frames, so a coroutine that
    // was started actually runs. A fixed count rather than a loop on IsRunning: the
    // scheduler is shared with the editor's own live watchers, and a fixture must not
    // wait on those.
    void Drain() {
      for (int i = 0; i < 50; i++) {
        Haste.Scheduler.Tick();
      }
    }

    [Test]
    public void OnDemandSourceDoesNotCrawlWhenRegistered() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.OnDemand);
      Drain();

      Assert.That(crawls, Is.EqualTo(0),
        "an on-demand source must not walk anything at domain load");
      Assert.That(watchers.IsStale(sourceName), Is.True,
        "a source that has never crawled is stale");
    }

    [Test]
    public void BackgroundSourceCrawlsWhenRegistered() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.Background);
      Drain();

      Assert.That(crawls, Is.EqualTo(1));
      Assert.That(watchers.IsStale(sourceName), Is.False);
    }

    [Test]
    public void ChangeEventMarksOnDemandSourceStaleInsteadOfCrawling() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.OnDemand);
      watchers.Refresh();
      Drain();
      Assert.That(crawls, Is.EqualTo(1), "the first Refresh crawls");

      // What EditorApplication.hierarchyChanged does, at the rate it does it.
      for (int i = 0; i < 50; i++) {
        watchers.RestartSource(sourceName);
        Drain();
      }

      Assert.That(crawls, Is.EqualTo(1),
        "50 change events must not start 50 crawls");
      Assert.That(watchers.IsStale(sourceName), Is.True);
    }

    [Test]
    public void ChangeEventStillRestartsABackgroundSource() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.Background);
      Drain();

      watchers.RestartSource(sourceName);
      Drain();

      Assert.That(crawls, Is.EqualTo(2),
        "the cheap, stable sources keep their warm index");
    }

    [Test]
    public void RefreshCrawlsAStaleOnDemandSource() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.OnDemand);
      watchers.RestartSource(sourceName);

      watchers.Refresh();
      Drain();

      Assert.That(crawls, Is.EqualTo(1));
      Assert.That(watchers.IsStale(sourceName), Is.False);
    }

    [Test]
    public void RefreshIsFreeWhenNothingHasChanged() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.OnDemand);
      watchers.Refresh();
      Drain();

      // Opening the palette repeatedly in a scene nobody has touched.
      watchers.Refresh();
      Drain();
      watchers.Refresh();
      Drain();

      Assert.That(crawls, Is.EqualTo(1));
    }

    [Test]
    public void RefreshDoesNotRestartACrawlAlreadyRunning() {
      watchers.AddSource(sourceName, true, SlowSource, HasteSourceCadence.OnDemand);
      watchers.Refresh();

      // One tick overruns the budget and yields, leaving the crawl mid-walk. Restarting
      // it here is what the storm was made of.
      Haste.Scheduler.Tick();
      Assert.That(watchers.IsStale(sourceName), Is.False);

      watchers.RestartSource(sourceName);
      watchers.Refresh();
      Drain();

      Assert.That(crawls, Is.EqualTo(1),
        "a walk already in progress is left to finish, not started over");
    }

    [Test]
    public void DisabledOnDemandSourceIsNotCrawledByRefresh() {
      watchers.AddSource(sourceName, false, CountedSource, HasteSourceCadence.OnDemand);
      watchers.Refresh();
      Drain();

      Assert.That(crawls, Is.EqualTo(0));
    }

    [Test]
    public void RebuildDefersAnOnDemandSourceRatherThanWalkingIt() {
      watchers.AddSource(sourceName, true, CountedSource, HasteSourceCadence.OnDemand);
      watchers.Refresh();
      Drain();

      // The Rebuild Index button, an ignore-rule edit, an upgrade.
      watchers.Rebuild();
      Drain();

      Assert.That(crawls, Is.EqualTo(1), "nothing may crawl behind a closed palette");
      Assert.That(watchers.IsStale(sourceName), Is.True, "but the next open must");

      watchers.Refresh();
      Drain();
      Assert.That(crawls, Is.EqualTo(2));
    }

    [Test]
    public void HierarchyIsTheOnDemandSourceAndTheOthersAreNot() {
      Assert.That(Haste.Watchers.CadenceOf(HasteHierarchySource.NAME),
        Is.EqualTo(HasteSourceCadence.OnDemand));

      foreach (var name in new[] { HasteProjectSource.NAME, HasteMenuItemSource.NAME,
                                   HasteLayoutSource.NAME }) {
        Assert.That(Haste.Watchers.CadenceOf(name),
          Is.EqualTo(HasteSourceCadence.Background),
          name + " is cheap and stable enough to keep warm");
      }
    }
  }

  // The map that turns a hierarchy result back into its GameObject. It is read while the
  // crawl that rebuilds it is still running, which is new: the walk now happens with the
  // palette open rather than in the background.
  [TestFixture]
  internal class HasteHierarchySceneMapTests {

    GameObject fixture;

    [SetUp]
    public void SetUp() {
      fixture = new GameObject("HasteSceneMapFixture");
    }

    [TearDown]
    public void TearDown() {
      if (fixture != null) {
        Object.DestroyImmediate(fixture);
      }
    }

    [Test]
    public void AbandonedCrawlLeavesThePreviousMapIntact() {
      foreach (var item in new HasteHierarchySource()) {
        // Complete one walk so there is a map to preserve.
      }

      var before = HasteHierarchySource.Scene;
      Assert.That(before.Count, Is.GreaterThan(0));

      // Start a walk and abandon it, the way Restart does.
      var abandoned = new HasteHierarchySource().GetEnumerator();
      abandoned.MoveNext();

      Assert.That(HasteHierarchySource.Scene, Is.SameAs(before),
        "a half-finished walk must not empty the map out from under the palette");
      Assert.That(HasteHierarchySource.Scene.Count, Is.EqualTo(before.Count));
    }

    [Test]
    public void CompletedCrawlPublishesTheNewMap() {
      var before = HasteHierarchySource.Scene;

      foreach (var item in new HasteHierarchySource()) {
      }

      Assert.That(HasteHierarchySource.Scene, Is.Not.SameAs(before));
      Assert.That(HasteHierarchySource.Scene.Values, Has.Some.EqualTo(fixture),
        "every walked GameObject is resolvable from the map it publishes");
    }
  }
}
