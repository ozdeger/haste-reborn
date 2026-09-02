using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // The context menu the actions pane shows.
  //
  // Build is pure, so the shape, the ordering and the submenu detection are all testable
  // without a live editor. The tests that DO use the editor are here because the whole
  // point of this feature is that the menu is read rather than guessed -- a test against a
  // hand-written list of paths would be testing the guess again.
  [TestFixture]
  internal class HasteMenuTreeTests {

    Object[] saved;

    [SetUp]
    public void SetUp() {
      saved = Selection.objects;
    }

    [TearDown]
    public void TearDown() {
      Selection.objects = saved;
    }

    [Test]
    public void BuildNestsOnSlashesAndOnlyLeavesAreExecutable() {
      var tree = HasteMenuTree.Build("Assets", new[] {
        "Assets/Open",
        "Assets/Create/Folder",
        "Assets/Create/Shader/Unlit Shader",
        "Assets/Delete",
      });

      Assert.That(tree.Children.Select(c => c.Label), Is.EqualTo(new[] { "Open", "Create", "Delete" }),
        "menu order is preserved -- a context menu that reshuffles between openings is " +
        "worse than one in an unfamiliar order");

      var open = tree.Children[0];
      Assert.That(open.IsSubmenu, Is.False);
      Assert.That(open.Path, Is.EqualTo("Assets/Open"));

      var create = tree.Children[1];
      Assert.That(create.IsSubmenu, Is.True);

      // "Assets/Create" is not something ExecuteMenuItem can run, so it carries no path.
      Assert.That(create.Path, Is.Null);
      Assert.That(create.Children.Select(c => c.Label), Is.EqualTo(new[] { "Folder", "Shader" }));

      var shader = create.Children[1];
      Assert.That(shader.IsSubmenu, Is.True);
      Assert.That(shader.Path, Is.Null);
      Assert.That(shader.Children.Single().Path, Is.EqualTo("Assets/Create/Shader/Unlit Shader"));
    }

    [Test]
    public void BuildIgnoresWhatIsNotUnderTheRoot() {
      var tree = HasteMenuTree.Build("Assets", new[] {
        "Assets/Open", "GameObject/Create Empty", "", null, "AssetsNotReally/Thing",
      });

      Assert.That(tree.Children.Select(c => c.Label), Is.EqualTo(new[] { "Open" }));
      Assert.That(HasteMenuTree.Build("Assets", null).Children, Is.Empty);
    }

    [Test]
    public void APathThatIsBothALeafAndASubmenuKeepsBoth() {
      // Unity really does this: "Assets/Create/Material" exists alongside
      // "Assets/Create/Rendering/Material". Within one branch, a node can be executable
      // and still have children.
      var tree = HasteMenuTree.Build("Assets", new[] {
        "Assets/Thing",
        "Assets/Thing/Deeper",
      });

      var thing = tree.Children.Single();
      Assert.That(thing.Path, Is.EqualTo("Assets/Thing"));
      Assert.That(thing.IsSubmenu, Is.True);
    }

    [Test]
    public void OnlyThingsThatCanBeRightClickedHaveAContextMenu() {
      Assert.That(HasteMenuTree.RootFor(
        new HasteItem("Assets/P/Popup.prefab", 0, HasteProjectSource.NAME)), Is.EqualTo("Assets"));
      Assert.That(HasteMenuTree.RootFor(
        new HasteItem("Main Camera", 0, HasteHierarchySource.NAME)), Is.EqualTo("GameObject"));

      // A menu item and a layout are not objects. Nothing right-clicks them, so the pane
      // keeps the hand-written actions for those.
      Assert.That(HasteMenuTree.RootFor(
        new HasteItem("File/Build Profiles", 0, HasteMenuItemSource.NAME)), Is.Null);
      Assert.That(HasteMenuTree.RootFor(
        new HasteItem("Window/Layouts/Tall", 0, HasteLayoutSource.NAME)), Is.Null);
      Assert.That(HasteMenuTree.RootFor(null), Is.Null);
    }

    [Test]
    public void TheLiveAssetsMenuIsTheProjectWindowsContextMenu() {
      var tree = HasteMenuTree.BuildLive("Assets");
      var labels = tree.Children.Select(c => c.Label).ToArray();

      // The entries a right-click is actually for. If Unity moves these, the pane should
      // fail here rather than quietly show a shorter menu.
      foreach (var expected in new[] { "Open", "Delete", "Rename", "Copy Path", "Create" }) {
        Assert.That(labels, Contains.Item(expected));
      }

      var create = tree.Children.Single(c => c.Label == "Create");
      Assert.That(create.IsSubmenu, Is.True, "Create is the recursive case this pane exists for");
      Assert.That(create.Children.Count, Is.GreaterThan(5));

      // Every executable node names a path the editor really has.
      var live = new System.Collections.Generic.HashSet<string>(
        HasteMenuItemSource.ReadPaths("Assets"));
      foreach (var child in tree.Children.Where(c => c.Path != null)) {
        Assert.That(live, Contains.Item(child.Path));
      }
    }

    [Test]
    public void ValidationFollowsTheSelectionWhichIsWhyThePaneSelectsFirst() {
      var tree = HasteMenuTree.BuildLive("Assets");

      Selection.objects = new Object[0];
      var withoutSelection = HasteMenuTree.EnabledChildren(tree).Select(c => c.Label).ToArray();

      var asset = AssetDatabase.LoadMainAssetAtPath("Packages/com.hastereborn.haste/package.json");
      Assert.That(asset, Is.Not.Null, "the probe asset moved");
      Selection.objects = new[] { asset };
      var withSelection = HasteMenuTree.EnabledChildren(tree).Select(c => c.Label).ToArray();

      // The whole reason ShowActions sets the selection before reading the menu: without
      // it, every entry that acts on the clicked row reads as disabled and is filtered
      // out, leaving a context menu with none of the context in it.
      Assert.That(withSelection.Length, Is.GreaterThan(withoutSelection.Length));
      foreach (var label in new[] { "Open", "Delete", "Rename" }) {
        Assert.That(withSelection, Contains.Item(label));
        Assert.That(withoutSelection, Has.No.Member(label));
      }
    }

    [Test]
    public void AGreyedOutMenuItemIsNotAResult() {
      // Measured on a stock 6000.3.17f1: every one of the 172 "Component/..." entries is
      // unavailable with nothing selected, because there is nothing to add a component to.
      // Offering them means offering rows that do nothing when you press Enter.
      Selection.objects = new Object[0];

      var component = "Component/Physics/Rigidbody";
      Assert.That(HasteMenuItemSource.ReadPaths("Component"), Contains.Item(component),
        "the probe menu item moved");
      Assert.That(HasteMenuItemSource.IsAvailable(component), Is.False);

      var go = new GameObject("haste-availability-probe");
      try {
        Selection.objects = new Object[] { go };
        Assert.That(HasteMenuItemSource.IsAvailable(component), Is.True,
          "with something to add a component to, it comes back");
      } finally {
        Object.DestroyImmediate(go);
        Selection.objects = new Object[0];
      }
    }

    [Test]
    public void HastesOwnActionsSurviveTheAvailabilityFilter() {
      // These are not real menu items -- HasteActions implements them -- so the editor
      // has no opinion and reports them disabled. Filtering on that alone would have made
      // every one of them vanish from search.
      Selection.objects = new Object[0];

      const string custom = "GameObject/Select Prefab";
      Assert.That(HasteMenuItemSource.IsCustomAction(custom), Is.True);
      Assert.That(Menu.GetEnabled(custom), Is.False, "not a real menu item, as expected");
      Assert.That(HasteMenuItemSource.IsAvailable(custom), Is.True);

      // Every custom action must be indexed, or the exemption is protecting nothing.
      var indexed = new HasteMenuItemSource().Select(i => i.path).ToArray();
      foreach (var path in indexed.Where(HasteMenuItemSource.IsCustomAction)) {
        Assert.That(HasteMenuItemSource.IsAvailable(path), Is.True, path);
      }
    }

    [Test]
    public void TheSearchDropsUnavailableMenuItems() {
      Selection.objects = new Object[0];

      var index = new HasteIndex();
      index.Add(new HasteItem("Component/Physics/Rigidbody", 0, HasteMenuItemSource.NAME));
      index.Add(new HasteItem("Assets/Rigidbody.prefab", 1, HasteProjectSource.NAME));

      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(new HasteSearch(index).Search("rigidbody", 100, promise));

      var paths = promise.Value.Select(r => r.Item.path).ToArray();
      Assert.That(paths, Contains.Item("Assets/Rigidbody.prefab"));
      Assert.That(paths, Has.No.Member("Component/Physics/Rigidbody"),
        "nothing is selected, so this menu item cannot be run and is not offered");
    }

    [Test]
    public void ProjectWideEntriesAreLeftOutOfAnItemsContextMenu() {
      // Unity's Assets menu is a menu-bar menu doing double duty as the context menu, so
      // it holds entries that act on the whole project alongside ones that act on the
      // asset. Only the second kind belongs in a palette that acts on one item.
      //
      // The test of which is which is measured, not editorial: an entry still enabled
      // with nothing selected cannot be acting on the selection.
      var asset = AssetDatabase.LoadMainAssetAtPath("Packages/com.hastereborn.haste/package.json");
      Assert.That(asset, Is.Not.Null, "the probe asset moved");
      Selection.objects = new[] { asset };

      var tree = HasteMenuTree.BuildLive("Assets");
      var shown = HasteMenuTree.VisibleChildren(tree, "Assets").Select(c => c.Label).ToArray();

      foreach (var gone in new[] { "Refresh", "Reimport All", "Import New Asset...",
                                   "Import Package", "Open C# Project", "Update UXML Schema",
                                   "View in Import Activity Window" }) {
        Assert.That(shown, Has.No.Member(gone), gone + " acts on the project, not the asset");
      }

      foreach (var kept in new[] { "Open", "Delete", "Rename", "Copy Path", "Reimport",
                                   "Find References In Project", "Properties..." }) {
        Assert.That(shown, Contains.Item(kept));
      }

      // A package's own project-wide tooling is caught by the same rule without the rule
      // being told its name, which is the reason to prefer it to a list of names.
      Assert.That(shown, Has.No.Member("Mobile Dependency Resolver"));
    }

    [Test]
    public void TheTwoFalsePositivesAndTheCreateMenuAreExempt() {
      var asset = AssetDatabase.LoadMainAssetAtPath("Packages/com.hastereborn.haste/package.json");
      Selection.objects = new[] { asset };

      var tree = HasteMenuTree.BuildLive("Assets");
      var shown = HasteMenuTree.VisibleChildren(tree, "Assets").Select(c => c.Label).ToArray();

      // 81 of the 119 Assets entries are under Create and every one is enabled with
      // nothing selected, so the rule alone would take the whole submenu.
      Assert.That(shown, Contains.Item("Create"));
      var create = tree.Children.Single(c => c.Label == "Create");
      Assert.That(HasteMenuTree.VisibleChildren(create, "Assets").Count, Is.GreaterThan(5));

      // The rule's only two false positives on a stock editor.
      Assert.That(shown, Contains.Item("Reveal in Finder"));
      Assert.That(shown, Contains.Item("Select Dependencies"));

      // And the short blocklist, for what the rule cannot reason about.
      Assert.That(shown, Has.No.Member("Create UPM Package..."));
      Assert.That(shown, Has.No.Member("Export As UPM Package..."));
    }

    [Test]
    public void TheRuleIsNotAppliedToTheHierarchyMenu() {
      // Measured: applying the empty-selection rule to GameObject leaves 3 of its 24
      // entries. "3D Object", "Light", "Camera", "Make Parent", "Move To View" and the
      // rest are not project-wide -- they have no [MenuItem] validate function, so the
      // editor reports them enabled at all times and the rule cannot see them for what
      // they are. Unity's Assets entries mostly do declare one, which is why it works
      // there and only there.
      var go = new GameObject("haste-menu-probe");
      try {
        Selection.objects = new Object[] { go };

        var tree = HasteMenuTree.BuildLive("GameObject");
        var shown = HasteMenuTree.VisibleChildren(tree, "GameObject").Select(c => c.Label).ToArray();

        // Every one of these is enabled with nothing selected, so the rule would take
        // all of them. Only entries the rule governs are asserted here -- "Make Parent",
        // "Move To View" and "Center On Children" are genuinely disabled in a headless
        // editor (no Scene view, one object selected, no children), and that is the
        // availability filter doing its job rather than anything to do with the rule.
        foreach (var kept in new[] { "2D Object", "3D Object", "Effects", "Light",
                                     "Camera", "Create Empty Child" }) {
          Assert.That(shown, Contains.Item(kept),
            "the rule must not be extended to GameObject without measuring it again");
        }
        Assert.That(shown.Length, Is.GreaterThan(12),
          "with the rule applied this drops to 3");
      } finally {
        Object.DestroyImmediate(go);
        Selection.objects = new Object[0];
      }
    }

    [Test]
    public void AnExemptionMatchesASubtreeButNotALongerName() {
      // "Assets/Create" must cover "Assets/Create/Folder" without also covering
      // "Assets/Create UPM Package..." -- which is on the blocklist precisely because it
      // is a different entry that happens to start the same way.
      var tree = HasteMenuTree.Build("Assets", new[] {
        "Assets/Create/Folder", "Assets/Create UPM Package...",
      });

      var shown = HasteMenuTree.VisibleChildren(tree, "Assets").Select(c => c.Label).ToArray();
      Assert.That(shown, Contains.Item("Create"));
      Assert.That(shown, Has.No.Member("Create UPM Package..."));
    }

    [Test]
    public void ASubmenuCountsAsEnabledWhenAnythingInsideItIs() {
      // Menu.GetEnabled answers for a leaf, because a submenu has no validate function to
      // run. Asking it about "Assets/Create" would drop the whole branch.
      var tree = HasteMenuTree.Build("Assets", new[] { "Assets/Create/Folder" });
      var create = tree.Children.Single();

      Assert.That(create.Path, Is.Null);
      Assert.That(HasteMenuTree.IsEnabled(create), Is.EqualTo(HasteMenuTree.IsEnabled(create.Children[0])));

      // A node with neither a path nor children cannot be enabled.
      Assert.That(HasteMenuTree.IsEnabled(new HasteMenuNode { Label = "Orphan" }), Is.False);
      Assert.That(HasteMenuTree.IsEnabled(null), Is.False);
    }
  }
}
