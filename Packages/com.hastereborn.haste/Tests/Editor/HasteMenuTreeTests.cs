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
