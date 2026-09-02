using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Haste {

  // Favourites and the flat multiplier they carry.
  //
  // The store is a ScriptableSingleton written to UserSettings, so it is the developer's
  // real favourites list. Every test here snapshots it and puts it back.
  [TestFixture]
  internal class HasteFavoriteTests {

    string[] saved;

    [SetUp]
    public void SetUp() {
      saved = HasteFavorites.instance.ToArray();
      HasteFavorites.instance.Clear();
    }

    [TearDown]
    public void TearDown() {
      HasteFavorites.instance.SetAll(saved);
    }

    [Test]
    public void TogglingAddsThenRemovesAndReportsTheNewState() {
      var item = new HasteItem("Assets/P/Popup.prefab", 0, HasteProjectSource.NAME);

      Assert.That(HasteFavorites.instance.Contains(item), Is.False);
      Assert.That(HasteFavorites.instance.Toggle(item), Is.True, "returns the state AFTER the call");
      Assert.That(HasteFavorites.instance.Contains(item), Is.True);
      Assert.That(HasteFavorites.instance.Count, Is.EqualTo(1));

      Assert.That(HasteFavorites.instance.Toggle(item), Is.False);
      Assert.That(HasteFavorites.instance.Contains(item), Is.False);
      Assert.That(HasteFavorites.instance.Count, Is.EqualTo(0));
    }

    [Test]
    public void AFavouriteSurvivesTheItemBeingReIndexed() {
      // The reason the key is "source|path" and not the HasteItem. HasteItem.GetHashCode
      // folds in `id`, which for a project asset is its position in enumeration order --
      // so an equality-keyed favourite would be silently lost on the next reimport.
      var before = new HasteItem("Assets/P/Popup.prefab", 17, HasteProjectSource.NAME);
      HasteFavorites.instance.Toggle(before);

      var after = new HasteItem("Assets/P/Popup.prefab", 4213, HasteProjectSource.NAME);
      Assert.That(after.GetHashCode(), Is.Not.EqualTo(before.GetHashCode()),
        "if these ever become equal this test is no longer proving anything");
      Assert.That(HasteFavorites.instance.Contains(after), Is.True);
    }

    [Test]
    public void TheSourceIsPartOfTheKeyBecauseAPathAloneIsNotUnique() {
      // Measured on 6000.3.17f1: the Layout source and the Menu Item source both yield
      // "Window/Layouts/Tall".
      var layout = new HasteItem("Window/Layouts/Tall", 0, HasteLayoutSource.NAME);
      var menu = new HasteItem("Window/Layouts/Tall", 0, HasteMenuItemSource.NAME);

      HasteFavorites.instance.Toggle(layout);

      Assert.That(HasteFavorites.instance.Contains(layout), Is.True);
      Assert.That(HasteFavorites.instance.Contains(menu), Is.False);
    }

    [Test]
    public void AnEmptyListCostsNothingAndAnswersFalse() {
      Assert.That(HasteFavorites.instance.Count, Is.EqualTo(0));
      Assert.That(HasteFavorites.instance.Contains(
        new HasteItem("Assets/Anything.prefab", 0, HasteProjectSource.NAME)), Is.False);
      Assert.That(HasteFavorites.instance.Contains(null), Is.False);
      Assert.That(HasteFavorites.For(null), Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void AFavouriteDoublesTheScoreOnTopOfEverythingElse() {
      var index = new HasteIndex();
      var plain = new HasteItem("Assets/A/Popup.prefab", 0, HasteProjectSource.NAME);
      var starred = new HasteItem("Assets/B/Popup.prefab", 1, HasteProjectSource.NAME);
      index.Add(plain);
      index.Add(starred);

      var terms = new[] { "popup" };
      Assert.That(HasteScoring.Score(plain, terms),
        Is.EqualTo(HasteScoring.Score(starred, terms)).Within(0.001f),
        "the two must be indistinguishable before favouriting, or this proves nothing");

      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));
      var unfavoured = promise.Value.Single(r => r.Item.path == starred.path).Score;

      HasteFavorites.instance.Toggle(starred);

      promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));

      Assert.That(promise.Value[0].Item.path, Is.EqualTo(starred.path),
        "the favourite should outrank an otherwise identical result");
      Assert.That(promise.Value[0].Score,
        Is.EqualTo(unfavoured * HasteFavorites.Multiplier).Within(0.01f),
        "flat, and multiplied on top of whatever the weights already did");
    }

    [Test]
    public void TheMultiplierStacksWithTheKindWeightRatherThanReplacingIt() {
      var weight = HasteWeights.Get(HasteKind.Prefab);
      try {
        HasteWeights.Set(HasteKind.Prefab, 0.5f);

        var item = new HasteItem("Assets/A/Popup.prefab", 0, HasteProjectSource.NAME);
        var index = new HasteIndex();
        index.Add(item);

        var promise = new Promise<IHasteResult[]>();
        HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));
        var weighted = promise.Value.Single().Score;

        HasteFavorites.instance.Toggle(item);
        promise = new Promise<IHasteResult[]>();
        HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));

        Assert.That(promise.Value.Single().Score,
          Is.EqualTo(weighted * HasteFavorites.Multiplier).Within(0.01f));
      } finally {
        HasteWeights.Set(HasteKind.Prefab, weight);
      }
    }

    static HasteKeyIntent Key(KeyCode key, bool alt, bool actionsMode = false, bool shift = false) {
      return HasteKeyMap.Resolve(key, false, shift, alt, actionsMode, false, false, true);
    }

    [Test]
    public void AltEnterFavouritesFromAnywhereInThePalette() {
      // A binding that works everywhere except one place is worse than one that works
      // nowhere, because the exception is the part nobody remembers.
      Assert.That(Key(KeyCode.Return, alt: true), Is.EqualTo(HasteKeyIntent.ToggleFavorite));
      Assert.That(Key(KeyCode.KeypadEnter, alt: true), Is.EqualTo(HasteKeyIntent.ToggleFavorite));
      Assert.That(Key(KeyCode.Return, alt: true, actionsMode: true),
        Is.EqualTo(HasteKeyIntent.ToggleFavorite));

      // And it must beat every other Enter binding rather than race them.
      Assert.That(Key(KeyCode.Return, alt: false), Is.EqualTo(HasteKeyIntent.Reveal));
      Assert.That(Key(KeyCode.Return, alt: false, actionsMode: true),
        Is.EqualTo(HasteKeyIntent.RunAction));
      Assert.That(Key(KeyCode.Return, alt: true, shift: true),
        Is.EqualTo(HasteKeyIntent.ToggleFavorite), "Alt wins over Shift+Enter's Open");
    }

    [Test]
    public void SceneObjectsCannotBeFavourited() {
      // A hierarchy favourite would be keyed on the object's path in the scene, which
      // changes when it is renamed, reparented, or when the scene closes. A favourite
      // that silently stops matching is worse than not offering one.
      var sceneObject = new HasteItem("Canvas/Popup", 0, HasteHierarchySource.NAME);

      Assert.That(HasteFavorites.CanFavorite(sceneObject), Is.False);
      Assert.That(HasteFavorites.instance.Toggle(sceneObject), Is.False);
      Assert.That(HasteFavorites.instance.Contains(sceneObject), Is.False);
      Assert.That(HasteFavorites.instance.Count, Is.EqualTo(0), "nothing was stored");
      Assert.That(HasteFavorites.For(sceneObject), Is.EqualTo(1.0f).Within(0.001f));

      // The stable sources, which is everything else Haste indexes.
      foreach (var source in new[] { HasteProjectSource.NAME, HasteMenuItemSource.NAME,
                                     HasteLayoutSource.NAME }) {
        Assert.That(HasteFavorites.CanFavorite(source), Is.True, source);
      }
    }

    [Test]
    public void RemovingAndClearingFromThePreferencesList() {
      var a = new HasteItem("Assets/A.prefab", 0, HasteProjectSource.NAME);
      var b = new HasteItem("Assets/B.prefab", 1, HasteProjectSource.NAME);
      HasteFavorites.instance.Toggle(a);
      HasteFavorites.instance.Toggle(b);

      var keys = HasteFavorites.instance.ToArray();
      Assert.That(keys.Length, Is.EqualTo(2));

      // What the preferences list renders each row from.
      Assert.That(HasteFavorites.PathOf(keys[0]), Is.EqualTo("Assets/A.prefab"));
      Assert.That(HasteFavorites.SourceOf(keys[0]), Is.EqualTo(HasteProjectSource.NAME));

      HasteFavorites.instance.RemoveKey(keys[0]);
      Assert.That(HasteFavorites.instance.Contains(a), Is.False);
      Assert.That(HasteFavorites.instance.Contains(b), Is.True);

      HasteFavorites.instance.Clear();
      Assert.That(HasteFavorites.instance.Count, Is.EqualTo(0));
    }
  }
}
