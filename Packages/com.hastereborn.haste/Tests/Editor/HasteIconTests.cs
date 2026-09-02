using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // The built-in editor icon names Haste uses for rows that have no asset icon.
  //
  // These are internal Unity resource names. Nothing checks them at compile time and a
  // wrong one fails silently -- a blank square where an icon should be -- so the only way
  // to know they are real is to resolve them against the running editor.
  [TestFixture]
  internal class HasteIconTests {

    [Test]
    public void EveryIconNameResolvesOnThisEditor() {
      foreach (var pair in HasteIcons.Names) {
        Assert.That(HasteIcons.For(pair.Key), Is.Not.Null,
          "no built-in icon named \"" + pair.Value + "\" for " + pair.Key);
      }
    }

    [Test]
    public void TheKindsWithNoAssetIconAllHaveOne() {
      // Menu items and layouts are not assets and have no object, so this table is the
      // only thing standing between them and a text badge.
      foreach (var kind in new[] { HasteKind.Menu, HasteKind.Component, HasteKind.Layout }) {
        Assert.That(HasteIcons.NameFor(kind), Is.Not.Null.And.Not.Empty, kind.ToString());
      }
    }

    [Test]
    public void EveryKindHasAnIcon() {
      // A kind with no entry falls back to the text badge, which is the thing this table
      // exists to stop happening.
      foreach (var kind in HasteKinds.All) {
        Assert.That(HasteIcons.NameFor(kind), Is.Not.Null, kind.ToString());
      }
    }

    [Test]
    public void DoNotWriteTheDarkSkinPrefix() {
      // IconContent picks the "d_" variant itself. Writing it here would pin the palette
      // to the dark skin's icon on a light editor.
      foreach (var pair in HasteIcons.Names) {
        Assert.That(pair.Value, Does.Not.StartWith("d_"), pair.Key.ToString());
      }
    }

    [Test]
    public void ToolsMenuItemsAreOrdinaryMenuItems() {
      // "Tools/..." had its own kind, its own "TL" badge and its own scope token. It is a
      // menu item under a root some package invented -- nothing more -- and per-root
      // weights now cover what the separate kind was really for.
      var tool = new HasteItem("Tools/Rebuild Atlas", 0, HasteMenuItemSource.NAME);
      var stock = new HasteItem("File/Build Profiles", 0, HasteMenuItemSource.NAME);

      Assert.That(HasteKinds.Classify(tool), Is.EqualTo(HasteKind.Menu));
      Assert.That(HasteKinds.Classify(tool), Is.EqualTo(HasteKinds.Classify(stock)));
      Assert.That(HasteKinds.Tag(tool), Is.EqualTo(HasteKinds.Tag(stock)));
      Assert.That(HasteIcons.For(HasteKinds.Classify(tool)),
        Is.SameAs(HasteIcons.For(HasteKinds.Classify(stock))));

      // The token still parses, so nobody's muscle memory breaks -- it just scopes to
      // menu items rather than to a kind that no longer exists.
      HasteKind kinds; string token;
      Assert.That(HasteKinds.SplitScope("t:tool atlas", out kinds, out token), Is.EqualTo("atlas"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Menu));
      Assert.That(token, Is.EqualTo("menu"));

      Assert.That(HasteKinds.All, Has.No.Member((HasteKind)(1 << 7)),
        "1 << 7 was Tool and is deliberately left as a gap");
    }
  }
}
