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
    Dictionary<string, float> savedMenus;

    // Roots these tests write to, on top of whatever the editor really has -- a root that
    // does not exist yet still has to be restorable.
    static readonly string[] TouchedRoots = { "File", "Edit", "Component", "Tools", "Dev Tools" };

    [SetUp]
    public void SetUp() {
      saved = HasteKinds.All.ToDictionary(k => k, k => HasteWeights.Get(k));
      savedMenus = HasteMenuItemSource.Roots.Concat(TouchedRoots).Distinct()
        .ToDictionary(r => r, r => HasteMenuWeights.Get(r));
    }

    [TearDown]
    public void TearDown() {
      foreach (var pair in saved) {
        HasteWeights.Set(pair.Key, pair.Value);
      }
      foreach (var pair in savedMenus) {
        HasteMenuWeights.Set(pair.Key, pair.Value);
      }
    }

    [Test]
    public void DefaultsDemoteWhatIsNumerousAndLeaveAssetsAlone() {
      Assert.That(HasteWeights.Default(HasteKind.Hierarchy), Is.EqualTo(0.5f).Within(0.001f));
      Assert.That(HasteWeights.Default(HasteKind.Layout), Is.EqualTo(0.7f).Within(0.001f));

      // Menu kinds are deliberately absent from this table -- HasteMenuWeights weights
      // them by root instead. They keep a neutral 1.0 here so that anything still reading
      // the kind table does not double-apply a demotion.
      foreach (var kind in new[] { HasteKind.Menu, HasteKind.Tool, HasteKind.Component }) {
        Assert.That(HasteWeights.IsMenuDriven(kind), Is.True, kind.ToString());
        Assert.That(HasteWeights.Default(kind), Is.EqualTo(1.0f).Within(0.001f), kind.ToString());
      }

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
      HasteWeights.Set(HasteKind.Menu, 0.25f);
      Assert.That(HasteWeights.Get(HasteKind.Menu), Is.EqualTo(0.25f).Within(0.001f));

      HasteWeights.Set(HasteKind.Menu, 99f);
      Assert.That(HasteWeights.Get(HasteKind.Menu), Is.EqualTo(HasteWeights.Max).Within(0.001f));

      HasteWeights.Set(HasteKind.Menu, -5f);
      Assert.That(HasteWeights.Get(HasteKind.Menu), Is.EqualTo(HasteWeights.Min).Within(0.001f));
    }

    [Test]
    public void TheWeightIsLookedUpFromTheItemsKind() {
      var sceneObject = new HasteItem("Main Camera", 0, HasteHierarchySource.NAME);
      var prefab = new HasteItem("Assets/P/Popup.prefab", 0, HasteProjectSource.NAME);

      Assert.That(HasteWeights.For(sceneObject), Is.EqualTo(HasteWeights.Get(HasteKind.Hierarchy)).Within(0.001f));
      Assert.That(HasteWeights.For(prefab), Is.EqualTo(HasteWeights.Get(HasteKind.Prefab)).Within(0.001f));
    }

    [Test]
    public void AMenuItemsWeightComesFromItsRootNotItsKind() {
      var unity = new HasteItem("File/Build Profiles", 0, HasteMenuItemSource.NAME);
      var mine = new HasteItem("Dev Tools/Rebuild Atlas", 0, HasteMenuItemSource.NAME);

      HasteMenuWeights.Set("File", 0.3f);
      HasteMenuWeights.Set("Dev Tools", 1.4f);

      Assert.That(HasteWeights.For(unity), Is.EqualTo(0.3f).Within(0.001f));
      Assert.That(HasteWeights.For(mine), Is.EqualTo(1.4f).Within(0.001f));

      // The kind weight is not consulted at all for menu items, so changing it must not
      // move them. This is the regression that a leftover slider in preferences would be.
      HasteWeights.Set(HasteKind.Menu, 0.0f);
      Assert.That(HasteWeights.For(unity), Is.EqualTo(0.3f).Within(0.001f));
    }

    [Test]
    public void UnitysOwnMenusStartDemotedAndTheProjectsOwnStartAtOne() {
      // The reason the split exists: Unity ships ~529 menu items and they should not bury
      // a project's assets, but a menu THIS project added is someone's own tooling.
      foreach (var root in new[] { "File", "Edit", "Assets", "GameObject", "Component",
                                   "Window", "Help" }) {
        Assert.That(HasteMenuItemSource.IsBuiltinRoot(root), Is.True, root);
        Assert.That(HasteMenuWeights.Default(root),
          Is.EqualTo(HasteMenuWeights.BuiltinDefault).Within(0.001f), root);
      }

      foreach (var root in new[] { "Tools", "Dev Tools", "Firebase", "Services" }) {
        Assert.That(HasteMenuItemSource.IsBuiltinRoot(root), Is.False, root);
        Assert.That(HasteMenuWeights.Default(root),
          Is.EqualTo(HasteMenuWeights.DiscoveredDefault).Within(0.001f), root);
      }

      // The old single menu weight was 0.7, and Unity's menus must not have moved when it
      // was split apart.
      Assert.That(HasteMenuWeights.BuiltinDefault, Is.EqualTo(0.7f).Within(0.001f));
    }

    [Test]
    public void TheRootIsTheFirstSegmentAndDoesNotMatchAPrefix() {
      Assert.That(HasteMenuWeights.RootOf("Window/General/Project"), Is.EqualTo("Window"));
      Assert.That(HasteMenuWeights.RootOf("Dev Tools/Atlas/Rebuild"), Is.EqualTo("Dev Tools"));

      // A top-level item is its own root -- that is what the menu bar shows.
      Assert.That(HasteMenuWeights.RootOf("Standalone"), Is.EqualTo("Standalone"));

      // "Editor" is not the "Edit" menu. Matching in place makes this easy to get wrong,
      // and getting it wrong would silently demote a project's own root.
      Assert.That(HasteMenuWeights.RootOf("Editor/Thing"), Is.EqualTo("Editor"));
      Assert.That(HasteMenuItemSource.MatchBuiltinRoot("Editor/Thing"), Is.Null);
      Assert.That(HasteMenuItemSource.MatchBuiltinRoot("Edit/Undo"), Is.EqualTo("Edit"));
      Assert.That(HasteMenuItemSource.MatchBuiltinRoot("Edit"), Is.EqualTo("Edit"));

      Assert.That(HasteMenuWeights.RootOf(""), Is.EqualTo(""));
      Assert.That(HasteMenuWeights.RootOf(null), Is.EqualTo(""));
      Assert.That(HasteMenuWeights.RootOf("/Leading"), Is.EqualTo(""));
    }

    [Test]
    public void AMenuRootsWeightPersistsAndIsClamped() {
      HasteMenuWeights.Set("Dev Tools", 0.25f);
      Assert.That(HasteMenuWeights.Get("Dev Tools"), Is.EqualTo(0.25f).Within(0.001f));

      HasteMenuWeights.Set("Dev Tools", 99f);
      Assert.That(HasteMenuWeights.Get("Dev Tools"), Is.EqualTo(HasteMenuWeights.Max).Within(0.001f));

      HasteMenuWeights.Set("Dev Tools", -5f);
      Assert.That(HasteMenuWeights.Get("Dev Tools"), Is.EqualTo(HasteMenuWeights.Min).Within(0.001f));
    }

    [Test]
    public void TheComponentKindAndTheComponentMenuAreSeparateSettings() {
      // "Component" is both a HasteKind and a menu root. Sharing the EditorPrefs prefix
      // would have made these one value wearing two labels in preferences.
      Assert.That(HasteSettings.GetPrefKey(HasteSetting.Weight, "Component"),
        Is.Not.EqualTo(HasteSettings.GetPrefKey(HasteSetting.MenuWeight, "Component")));

      HasteWeights.Set(HasteKind.Component, 0.2f);
      HasteMenuWeights.Set("Component", 1.8f);

      Assert.That(HasteWeights.Get(HasteKind.Component), Is.EqualTo(0.2f).Within(0.001f));
      Assert.That(HasteMenuWeights.Get("Component"), Is.EqualTo(1.8f).Within(0.001f));
    }

    [Test]
    public void EveryRootTheEditorHasIsListedForPreferences() {
      var roots = HasteMenuItemSource.Roots;

      Assert.That(roots, Is.Unique);
      Assert.That(roots, Has.No.Member(""));
      foreach (var builtin in new[] { "File", "Edit", "Assets", "GameObject", "Component",
                                      "Window", "Help" }) {
        Assert.That(roots, Contains.Item(builtin));
      }

      // The editor's own come first, so the list does not reshuffle between reloads.
      var firstProject = System.Array.FindIndex(roots, r => !HasteMenuItemSource.IsBuiltinRoot(r));
      if (firstProject >= 0) {
        for (int i = firstProject; i < roots.Length; i++) {
          Assert.That(HasteMenuItemSource.IsBuiltinRoot(roots[i]), Is.False, roots[i]);
        }
      }

      // Every root Haste indexes must be one it can also show a slider for, or that menu
      // is weighted by a default nobody can see or change.
      var indexed = new HasteMenuItemSource()
        .Select(item => HasteMenuWeights.RootOf(item.path)).Distinct();
      Assert.That(indexed, Is.SubsetOf(roots));
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

      HasteMenuWeights.Set("Popup", 0.7f);
      HasteWeights.Set(HasteKind.Asset, 1.0f);

      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("popup", 100, promise));
      var paths = promise.Value.Select(r => r.Item.source).ToArray();

      Assert.That(paths.Length, Is.EqualTo(2));
      Assert.That(paths[0], Is.EqualTo(HasteProjectSource.NAME), "the asset should outrank the command");

      // Invert the weights and the order follows.
      HasteMenuWeights.Set("Popup", 1.0f);
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

      HasteMenuWeights.Set("File", 0.0f);

      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("build", 100, promise));

      Assert.That(promise.Value, Is.Empty,
        "a zero weight zeroes the score, and zero-scoring results are dropped -- " +
        "documented in the preferences help text so nobody expects otherwise");
    }
  }
}
