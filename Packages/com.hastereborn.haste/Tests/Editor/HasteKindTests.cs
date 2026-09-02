using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Haste {

  // The presentation taxonomy behind the row badges and the scope tokens.
  //
  // Haste indexes four sources; the design asks for finer buckets, splitting Project by
  // file type and Menu Item by menu root. These pin that mapping, and the parsing of the
  // token prefixes that select it.
  [TestFixture]
  internal class HasteKindTests {

    static HasteItem Project(string path) { return new HasteItem(path, 0, HasteProjectSource.NAME); }
    static HasteItem Menu(string path) { return new HasteItem(path, 0, HasteMenuItemSource.NAME); }

    [Test]
    public void Classify_SplitsProjectAssetsByFileType() {
      Assert.That(HasteKinds.Classify(Project("Assets/Prefabs/Popup.prefab")), Is.EqualTo(HasteKind.Prefab));
      Assert.That(HasteKinds.Classify(Project("Assets/Scenes/Dev.unity")), Is.EqualTo(HasteKind.Scene));
      Assert.That(HasteKinds.Classify(Project("Assets/Scripts/Player.cs")), Is.EqualTo(HasteKind.Script));
      Assert.That(HasteKinds.Classify(Project("Assets/Sprites/Icon.png")), Is.EqualTo(HasteKind.Texture));

      // Anything without a recognised extension, and folders, stay generic.
      Assert.That(HasteKinds.Classify(Project("Assets/Folder")), Is.EqualTo(HasteKind.Asset));
      Assert.That(HasteKinds.Classify(Project("Assets/Data/Config.asset")), Is.EqualTo(HasteKind.Asset));
      Assert.That(HasteKinds.Classify(Project("Assets/Notes.txt")), Is.EqualTo(HasteKind.Asset));
    }

    [Test]
    public void Classify_RecognisesTheAssetTypesTheChipsOffer() {
      var expected = new System.Collections.Generic.Dictionary<string, HasteKind> {
        { "Assets/Art/Hero.png",            HasteKind.Texture },
        { "Assets/Art/Hero.tga",            HasteKind.Texture },
        { "Assets/Art/Hero.psd",            HasteKind.Texture },
        { "Assets/Sfx/Hit.wav",             HasteKind.Audio },
        { "Assets/Sfx/Theme.mp3",           HasteKind.Audio },
        { "Assets/Sfx/Amb.ogg",             HasteKind.Audio },
        { "Assets/Anim/Walk.anim",          HasteKind.Animation },
        { "Assets/Anim/Hero.controller",    HasteKind.Animator },
        { "Assets/Mat/Wall.mat",            HasteKind.Material },
        { "Assets/Models/Hero.fbx",         HasteKind.Model },
        { "Assets/Shaders/Water.shader",    HasteKind.Shader },
        { "Assets/Fonts/Inter.ttf",         HasteKind.Font },
      };

      foreach (var pair in expected) {
        Assert.That(HasteKinds.Classify(Project(pair.Key)), Is.EqualTo(pair.Value), pair.Key);
      }

      // A folder called "prefab" is not a prefab: only the extension counts.
      Assert.That(HasteKinds.Classify(Project("Assets/prefab/Thing.txt")), Is.EqualTo(HasteKind.Asset));
      // And a dot in a folder name is not an extension.
      Assert.That(HasteKinds.Classify(Project("Assets/v1.2/Thing")), Is.EqualTo(HasteKind.Asset));
    }

    [Test]
    public void SplitScope_UnderstandsTheProjectWindowsTSyntax() {
      HasteKind kinds; string token;

      // "t:" is what Unity's own Project search uses, so it is what people already type,
      // and it is what the chips insert.
      Assert.That(HasteKinds.SplitScope("t:prefab popup", out kinds, out token), Is.EqualTo("popup"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Prefab));
      Assert.That(token, Is.EqualTo("prefab"));

      Assert.That(HasteKinds.SplitScope("t:texture ", out kinds, out token), Is.EqualTo(""));
      Assert.That(kinds, Is.EqualTo(HasteKind.Texture));

      // With the trailing space, which is the delimiter -- see the test below for why the
      // name alone is deliberately not enough.
      Assert.That(HasteKinds.SplitScope("t:audio ", out kinds, out token), Is.EqualTo(""));
      Assert.That(kinds, Is.EqualTo(HasteKind.Audio));

      // Aliases, because "audioclip" is Unity's name and "audio" is what people type.
      foreach (var alias in new[] { "audio", "audioclip", "sound" }) {
        HasteKinds.SplitScope("t:" + alias + " x", out kinds, out token);
        Assert.That(kinds, Is.EqualTo(HasteKind.Audio), alias);
      }

      // A half-typed name is not a scope yet, so the query keeps working as you type.
      Assert.That(HasteKinds.SplitScope("t:pre", out kinds, out token), Is.EqualTo("t:pre"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Any));
      Assert.That(HasteKinds.SplitScope("t:banana x", out kinds, out token), Is.EqualTo("t:banana x"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Any));
    }

    [Test]
    public void Classify_SplitsMenuItemsByRoot() {
      Assert.That(HasteKinds.Classify(Menu("Component/Physics/Rigidbody")), Is.EqualTo(HasteKind.Component));
      Assert.That(HasteKinds.Classify(Menu("Tools/Atlas/Rebuild")), Is.EqualTo(HasteKind.Tool));
      Assert.That(HasteKinds.Classify(Menu("File/Build Profiles")), Is.EqualTo(HasteKind.Command));
      Assert.That(HasteKinds.Classify(Menu("Edit/Project Settings...")), Is.EqualTo(HasteKind.Command));
    }

    [Test]
    public void Classify_KeepsTheOtherTwoSourcesWhole() {
      Assert.That(HasteKinds.Classify(new HasteItem("Main Camera", 0, HasteHierarchySource.NAME)),
        Is.EqualTo(HasteKind.Hierarchy));
      Assert.That(HasteKinds.Classify(new HasteItem("Default", 0, HasteLayoutSource.NAME)),
        Is.EqualTo(HasteKind.Layout));
      Assert.That(HasteKinds.Classify(null), Is.EqualTo(HasteKind.None));
    }

    [Test]
    public void Tag_UsesTheExtensionForPlainAssetsAndAFixedLabelOtherwise() {
      // The badge stays the real extension for assets -- PNG and TGA are more use than a
      // shared "TEX" would be, even though both classify as Texture.
      Assert.That(HasteKinds.Tag(Project("Assets/Sprites/Icon.png")), Is.EqualTo("PNG"));
      Assert.That(HasteKinds.Tag(Project("Assets/Sprites/Icon.tga")), Is.EqualTo("TGA"));
      Assert.That(HasteKinds.Tag(Project("Assets/Anim/Walk.anim")), Is.EqualTo("ANIM"));
      Assert.That(HasteKinds.Tag(Project("Assets/Mat/Wall.material")), Is.EqualTo("MATE"), "over-long extensions are clipped");
      Assert.That(HasteKinds.Tag(Project("Assets/Folder")), Is.EqualTo("ASS"));
      Assert.That(HasteKinds.Tag(Project("Assets/P/Popup.prefab")), Is.EqualTo("PRE"));
      Assert.That(HasteKinds.Tag(Menu("Component/Physics/Rigidbody")), Is.EqualTo("CMP"));
      Assert.That(HasteKinds.Tag(new HasteItem("Main Camera", 0, HasteHierarchySource.NAME)), Is.EqualTo("GO"));

      // A menu item ending in "..." must not be read as having an extension; see the
      // ellipsis fix in HasteStringUtils.
      Assert.That(HasteKinds.Tag(Menu("File/Build Settings...")), Is.EqualTo("CMD"));
    }

    [Test]
    public void SplitScope_PeelsALeadingToken() {
      HasteKind kinds; string token;

      Assert.That(HasteKinds.SplitScope("prefab:popup", out kinds, out token), Is.EqualTo("popup"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Prefab));
      Assert.That(token, Is.EqualTo("prefab"));

      // Sigils bind without a colon; word tokens need one.
      Assert.That(HasteKinds.SplitScope(">build", out kinds, out token), Is.EqualTo("build"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Command | HasteKind.Tool));
      Assert.That(HasteKinds.SplitScope("#canvas", out kinds, out token), Is.EqualTo("canvas"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Component));

      // Whitespace after the token is eaten, so "h: main camera" scopes and keeps 2 terms.
      Assert.That(HasteKinds.SplitScope("h:  main camera", out kinds, out token), Is.EqualTo("main camera"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Hierarchy));
    }

    [Test]
    public void SplitScope_DoesNotCommitWhileTheTypeNameIsStillBeingTyped() {
      HasteKind kinds; string token;

      // The reported bug: "t:s" locked to SCENE on the way to "t:script", because "s" was
      // a valid alias and the scope committed the moment the text parsed.
      foreach (var partial in new[] { "t:s", "t:sc", "t:scr", "t:scrip", "t:script" }) {
        Assert.That(HasteKinds.SplitScope(partial, out kinds, out token), Is.EqualTo(partial), partial);
        Assert.That(kinds, Is.EqualTo(HasteKind.Any), partial + " committed too early");
      }

      // It commits on the space, which is what the chips insert and what typing a query
      // after the tag produces anyway.
      Assert.That(HasteKinds.SplitScope("t:script ", out kinds, out token), Is.EqualTo(""));
      Assert.That(kinds, Is.EqualTo(HasteKind.Script));

      // The same trap on every alias that is a prefix of a longer one.
      foreach (var pair in new[] {
        new[] { "t:anim", "t:animator " },
        new[] { "t:audio", "t:audioclip " },
        new[] { "t:mat", "t:material " },
      }) {
        HasteKinds.SplitScope(pair[0], out kinds, out token);
        Assert.That(kinds, Is.EqualTo(HasteKind.Any), pair[0] + " must stay reachable");
      }

      Assert.That(HasteKinds.SplitScope("t:animator ", out kinds, out token), Is.EqualTo(""));
      Assert.That(kinds, Is.EqualTo(HasteKind.Animator));
      Assert.That(HasteKinds.SplitScope("t:anim ", out kinds, out token), Is.EqualTo(""));
      Assert.That(kinds, Is.EqualTo(HasteKind.Animation));
    }

    [Test]
    public void SplitScope_HasNoAmbiguousSingleLetterTypeNames() {
      HasteKind kinds; string token;

      // "a" begins anim, animator, audio AND asset; "s" begins scene, script, shader,
      // sound and sprite. Any winner would be arbitrary and the losers unreachable.
      foreach (var letter in new[] { "a", "s" }) {
        HasteKinds.SplitScope("t:" + letter + " x", out kinds, out token);
        Assert.That(kinds, Is.EqualTo(HasteKind.Any), "t:" + letter + " should not resolve");
      }
    }

    [Test]
    public void SplitScope_LeavesAnythingThatIsNotATokenAlone() {
      HasteKind kinds; string token;

      // An unknown word before a colon is part of the query, not a scope.
      Assert.That(HasteKinds.SplitScope("banana:x", out kinds, out token), Is.EqualTo("banana:x"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Any));
      Assert.That(token, Is.Null);

      Assert.That(HasteKinds.SplitScope("main camera", out kinds, out token), Is.EqualTo("main camera"));
      Assert.That(kinds, Is.EqualTo(HasteKind.Any));
      Assert.That(HasteKinds.SplitScope("", out kinds, out token), Is.EqualTo(""));
      Assert.That(kinds, Is.EqualTo(HasteKind.Any));
    }

    [Test]
    public void Matches_IsTheFilterUsedBySearch() {
      var prefab = Project("Assets/P/Popup.prefab");
      var script = Project("Assets/S/Popup.cs");

      Assert.That(HasteKinds.Matches(HasteKind.Any, prefab), Is.True);
      Assert.That(HasteKinds.Matches(HasteKind.Prefab, prefab), Is.True);
      Assert.That(HasteKinds.Matches(HasteKind.Prefab, script), Is.False);
      // A token naming several kinds matches any of them.
      Assert.That(HasteKinds.Matches(HasteKind.Command | HasteKind.Tool, Menu("Tools/X/Y")), Is.True);
    }

    [Test]
    public void Filter_NarrowsAnAlreadyBuiltResultSet() {
      // The recency list is handed over whole rather than searched, so a scope has to be
      // applied to it afterwards. Without this, choosing a type chip left every unrelated
      // recent on screen and the scope appeared to do nothing.
      var results = new IHasteResult[] {
        new HasteItem("Assets/S/Player.cs", 0, HasteProjectSource.NAME).GetResult(1f, new string[0]),
        new HasteItem("Assets/P/Popup.prefab", 0, HasteProjectSource.NAME).GetResult(1f, new string[0]),
        new HasteItem("Main Camera", 0, HasteHierarchySource.NAME).GetResult(1f, new string[0]),
        new HasteItem("File/Build Profiles", 0, HasteMenuItemSource.NAME).GetResult(1f, new string[0]),
      };

      Assert.That(HasteKinds.Filter(results, HasteKind.Script).Select(r => r.Item.path).ToArray(),
        Is.EqualTo(new[] { "Assets/S/Player.cs" }));
      Assert.That(HasteKinds.Filter(results, HasteKind.Hierarchy).Select(r => r.Item.path).ToArray(),
        Is.EqualTo(new[] { "Main Camera" }));

      // A token naming several kinds keeps all of them.
      Assert.That(HasteKinds.Filter(results, HasteKind.Prefab | HasteKind.Command).Length, Is.EqualTo(2));

      // Any is a pass-through, and returns the same array rather than copying it.
      Assert.That(HasteKinds.Filter(results, HasteKind.Any), Is.SameAs(results));

      // Order is preserved: recents are ranked by what was actually picked.
      Assert.That(HasteKinds.Filter(results, HasteKind.Script | HasteKind.Prefab)
        .Select(r => r.Item.path).ToArray(),
        Is.EqualTo(new[] { "Assets/S/Player.cs", "Assets/P/Popup.prefab" }));

      Assert.That(HasteKinds.Filter(null, HasteKind.Script), Is.Empty);
      Assert.That(HasteKinds.Filter(results, HasteKind.Font), Is.Empty);
    }

    // ----------------------------------------------------------- item actions

    static System.Collections.Generic.List<HasteItemAction> ActionsFor(string path, string source) {
      return HasteItemActions.For(new HasteItem(path, 0, source).GetResult(0f, new string[0]));
    }

    static string[] LabelsFor(string path, string source) {
      return ActionsFor(path, source).Select(a => a.Label).ToArray();
    }

    [Test]
    public void Actions_LeadWithWhatEnterActuallyDoes() {
      // The first entry used to be called "Open" for everything, which was wrong twice
      // over: Enter reveals rather than opens, and for a menu item it runs. Open is now
      // Shift+Enter and only appears where there is something to open.
      Assert.That(ActionsFor("Assets/Thing.prefab", HasteProjectSource.NAME)[0].Label,
        Is.EqualTo("Reveal in Project window"));
      Assert.That(ActionsFor("Main Camera", HasteHierarchySource.NAME)[0].Label,
        Is.EqualTo("Reveal in Hierarchy"));
      Assert.That(ActionsFor("File/Build Profiles", HasteMenuItemSource.NAME)[0].Label,
        Is.EqualTo("Run"));
      Assert.That(ActionsFor("Default", HasteLayoutSource.NAME)[0].Label,
        Is.EqualTo("Switch to layout"));

      foreach (var source in new[] { HasteProjectSource.NAME, HasteHierarchySource.NAME,
                                     HasteMenuItemSource.NAME, HasteLayoutSource.NAME }) {
        Assert.That(ActionsFor("Assets/Thing.prefab", source)[0].Keys, Is.EqualTo("↵"));
      }

      // An asset can always be opened; a menu command never can.
      Assert.That(LabelsFor("Assets/Thing.prefab", HasteProjectSource.NAME), Contains.Item("Open"));
      Assert.That(LabelsFor("File/Build Profiles", HasteMenuItemSource.NAME), Has.No.Member("Open"));
      Assert.That(ActionsFor("Assets/Thing.prefab", HasteProjectSource.NAME)
        .Single(a => a.Label == "Open").Keys, Is.EqualTo("⇧↵"));
    }

    [Test]
    public void Actions_AreScopedToWhatTheItemActuallyIs() {
      var asset = LabelsFor("Assets/Sprites/Icon.png", HasteProjectSource.NAME);
      Assert.That(asset, Contains.Item("Reveal in Project window"));
      Assert.That(asset, Contains.Item("Copy GUID"));
      Assert.That(asset, Contains.Item("Duplicate"));
      Assert.That(asset, Contains.Item("Delete"));

      // A GameObject has no asset path, so the asset-only actions must not be offered --
      // Copy GUID and Duplicate would operate on a path that does not exist.
      var hierarchy = LabelsFor("Main Camera", HasteHierarchySource.NAME);
      Assert.That(hierarchy, Contains.Item("Reveal in Hierarchy"));
      Assert.That(hierarchy, Has.No.Member("Copy GUID"));
      Assert.That(hierarchy, Has.No.Member("Duplicate"));
      Assert.That(hierarchy, Has.No.Member("Delete"));

      // A menu command is neither: run it, or copy its path. Nothing else applies.
      Assert.That(LabelsFor("File/Build Profiles", HasteMenuItemSource.NAME),
        Is.EqualTo(new[] { "Run", "Copy Path" }));
    }

    [Test]
    public void Actions_MarkTheDestructiveOneAndKeepClipboardActionsInPlace() {
      var actions = ActionsFor("Assets/Sprites/Icon.png", HasteProjectSource.NAME);

      var delete = actions.Single(a => a.Label == "Delete");
      Assert.That(delete.Destructive, Is.True, "Delete is drawn in red on the strength of this");
      Assert.That(delete.ClosesWindow, Is.True,
        "it opens a confirmation dialog, and the palette dismisses on focus loss -- " +
        "running it in place would pull the window out from under the dialog");

      foreach (var copy in actions.Where(a => a.Label.StartsWith("Copy"))) {
        Assert.That(copy.ClosesWindow, Is.False, copy.Label + " should leave the palette open");
        Assert.That(copy.Confirmation, Is.Not.Null.And.Not.Empty, "the flash needs something to say");
      }

      Assert.That(actions.Select(a => a.Run), Has.None.Null, "an action with nothing to run");
    }

    [Test]
    public void Actions_NameTheFileBrowserAfterThePlatformAtRuntime() {
      // Runtime check, never a compile symbol: a Windows-built editor assembly bakes in
      // the compiling editor's symbol (HANDOFF 6.3).
      var expected = Application.platform == RuntimePlatform.OSXEditor
        ? "Show in Finder" : "Show in Explorer";
      Assert.That(LabelsFor("Assets/Sprites/Icon.png", HasteProjectSource.NAME),
        Contains.Item(expected));
    }

    // ------------------------------------------------ per-part highlighting

    [Test]
    public void Highlight_OfOnePartSkipsTermsThatDoNotOccurInIt() {
      // The row draws the name and the directory separately, so a term can be absent from
      // the part being drawn. Passing it to GetWeightedSubsequence would throw.
      var terms = new[] { "assets", "popup" };

      Assert.That(() => HasteStringUtils.GetHighlightIndices("Popup.prefab", terms), Throws.Nothing);

      // "assets" is not a subsequence of this part, so only "popup" highlights.
      //
      // Note where the last "p" lands: on the "p" of ".prefab", not the one that ends
      // "Popup". That is GetWeightedSubsequence preferring word boundaries, which is the
      // behaviour that makes "mc" bold the M and C of "Mesh Collider" -- pinned by
      // Highlight_PrefersWordBoundaries. It reads oddly on a short name shown by itself,
      // which the new row design does far more than the old full-path label did.
      Assert.That(HasteStringUtils.BoldLabel("Popup.prefab",
        HasteStringUtils.GetHighlightIndices("Popup.prefab", terms), "[", "]"),
        Is.EqualTo("[P][o][p][u]p.[p]refab"));

      Assert.That(HasteStringUtils.GetHighlightIndices("Popup.prefab", new[] { "zzz" }), Is.Empty);
      Assert.That(HasteStringUtils.GetHighlightIndices("", terms), Is.Empty);
      Assert.That(HasteStringUtils.GetHighlightIndices("Popup.prefab", null), Is.Empty);
    }

    [Test]
    public void Directory_IsWhatTheRowShowsOppositeTheName() {
      Assert.That(HasteStringUtils.GetDirectory("Assets/Sprites/Icon.png"), Is.EqualTo("Assets/Sprites"));
      Assert.That(HasteStringUtils.GetDirectory("Assets/Icon.png"), Is.EqualTo("Assets"));
      Assert.That(HasteStringUtils.GetDirectory("Main Camera"), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetDirectory(""), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetDirectory("Component/Physics/Rigidbody"), Is.EqualTo("Component/Physics"));
    }
  }
}
