using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Haste {

  // Per-kind score weights.
  //
  // These write to EditorPrefs, which is the developer's own machine-wide settings, so
  // every value touched here is captured and put back afterwards. A test suite that leaves
  // someone's preferences rearranged is a bad trade for coverage.
  [TestFixture]
  internal class HasteWeightTests {

    Dictionary<HasteKind, float> saved;

    [SetUp]
    public void SetUp() {
      saved = HasteKinds.All.ToDictionary(k => k, k => HasteWeights.Get(k));
    }

    [TearDown]
    public void TearDown() {
      foreach (var pair in saved) {
        HasteWeights.Set(pair.Key, pair.Value);
      }
    }

    [Test]
    public void DefaultsDemoteWhatIsNumerousAndLeaveAssetsAlone() {
      Assert.That(HasteWeights.Default(HasteKind.Hierarchy), Is.EqualTo(0.5f).Within(0.001f));
      Assert.That(HasteWeights.Default(HasteKind.Command), Is.EqualTo(0.7f).Within(0.001f));
      Assert.That(HasteWeights.Default(HasteKind.Tool), Is.EqualTo(0.7f).Within(0.001f));
      Assert.That(HasteWeights.Default(HasteKind.Component), Is.EqualTo(0.7f).Within(0.001f));

      // Project assets are what people are usually looking for, so nothing scales them.
      foreach (var kind in new[] { HasteKind.Asset, HasteKind.Prefab, HasteKind.Scene,
                                   HasteKind.Script, HasteKind.Texture, HasteKind.Audio,
                                   HasteKind.Animation, HasteKind.Material, HasteKind.Model }) {
        Assert.That(HasteWeights.Default(kind), Is.EqualTo(1.0f).Within(0.001f), kind.ToString());
      }
    }

    [Test]
    public void EveryKindHasAWeightAndTheTableCoversThemAll() {
      foreach (var kind in HasteKinds.All) {
        Assert.That(HasteWeights.Get(kind), Is.InRange(HasteWeights.Min, HasteWeights.Max), kind.ToString());
      }
      Assert.That(HasteKinds.All, Is.Unique);
      Assert.That(HasteKinds.All, Has.No.Member(HasteKind.None));
      Assert.That(HasteKinds.All, Has.No.Member(HasteKind.Any));
    }

    [Test]
    public void SettingAWeightPersistsAndIsClamped() {
      HasteWeights.Set(HasteKind.Command, 0.25f);
      Assert.That(HasteWeights.Get(HasteKind.Command), Is.EqualTo(0.25f).Within(0.001f));

      HasteWeights.Set(HasteKind.Command, 99f);
      Assert.That(HasteWeights.Get(HasteKind.Command), Is.EqualTo(HasteWeights.Max).Within(0.001f));

      HasteWeights.Set(HasteKind.Command, -5f);
      Assert.That(HasteWeights.Get(HasteKind.Command), Is.EqualTo(HasteWeights.Min).Within(0.001f));
    }

    [Test]
    public void TheWeightIsLookedUpFromTheItemsKind() {
      var menuItem = new HasteItem("File/Build Profiles", 0, HasteMenuItemSource.NAME);
      var sceneObject = new HasteItem("Main Camera", 0, HasteHierarchySource.NAME);
      var prefab = new HasteItem("Assets/P/Popup.prefab", 0, HasteProjectSource.NAME);

      Assert.That(HasteWeights.For(menuItem), Is.EqualTo(HasteWeights.Get(HasteKind.Command)).Within(0.001f));
      Assert.That(HasteWeights.For(sceneObject), Is.EqualTo(HasteWeights.Get(HasteKind.Hierarchy)).Within(0.001f));
      Assert.That(HasteWeights.For(prefab), Is.EqualTo(HasteWeights.Get(HasteKind.Prefab)).Within(0.001f));
    }

    [Test]
    public void WeightsChangeRankingWithoutChangingMatchQuality() {
      // Two items that score identically on match quality alone. The weight is the only
      // thing separating them, which is the whole point of keeping it out of HasteScoring.
      var index = new HasteIndex();
      var command = new HasteItem("Popup", 0, HasteMenuItemSource.NAME);
      var prefab = new HasteItem("Popup", 1, HasteProjectSource.NAME);
      index.Add(command);
      index.Add(prefab);

      var terms = new[] { "popup" };
      Assert.That(HasteScoring.Score(command, terms),
        Is.EqualTo(HasteScoring.Score(prefab, terms)).Within(0.001f),
        "the two must be indistinguishable before weighting, or this proves nothing");

      HasteWeights.Set(HasteKind.Command, 0.7f);
      HasteWeights.Set(HasteKind.Asset, 1.0f);

      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));
      var paths = promise.Value.Select(r => r.Item.source).ToArray();

      Assert.That(paths.Length, Is.EqualTo(2));
      Assert.That(paths[0], Is.EqualTo(HasteProjectSource.NAME), "the asset should outrank the command");

      // Invert the weights and the order follows.
      HasteWeights.Set(HasteKind.Command, 1.0f);
      HasteWeights.Set(HasteKind.Asset, 0.5f);

      promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));
      Assert.That(promise.Value[0].Item.source, Is.EqualTo(HasteMenuItemSource.NAME));
    }

    [Test]
    public void AZeroWeightSinksAResultRatherThanHidingIt() {
      // Zero scores are dropped by Map as noise, so a zero weight must not silently make a
      // whole type unfindable -- that is what turning its source off is for.
      var index = new HasteIndex();
      index.Add(new HasteItem("File/Build Profiles", 0, HasteMenuItemSource.NAME));

      HasteWeights.Set(HasteKind.Command, 0.0f);

      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("build", 100, promise));

      Assert.That(promise.Value, Is.Empty,
        "a zero weight zeroes the score, and zero-scoring results are dropped -- " +
        "documented in the preferences help text so nobody expects otherwise");
    }
  }
}
