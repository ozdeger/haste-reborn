using System.Linq;
using NUnit.Framework;

namespace Haste {

  // The ignore matcher.
  //
  // Worth testing properly because every failure here is silent: an over-broad rule makes
  // files unfindable with no error, and an under-broad one just leaves noise. The version
  // this replaced did `path.IndexOf(rule) == 0` -- culture sensitive, and with no segment
  // boundary, so "Assets/Plugins" also swallowed "Assets/PluginsCustom".
  [TestFixture]
  internal class HasteIgnoreTests {

    static bool Ignored(string path, params string[] rules) {
      return HasteIgnoreRules.IsIgnored(path, rules);
    }

    [Test]
    public void APathRuleMatchesOnlyAtASegmentBoundary() {
      Assert.That(Ignored("Assets/Plugins", "Assets/Plugins"), Is.True);
      Assert.That(Ignored("Assets/Plugins/Feel/Demo.cs", "Assets/Plugins"), Is.True);

      // The bug the old matcher had.
      Assert.That(Ignored("Assets/PluginsCustom/Thing.cs", "Assets/Plugins"), Is.False);
      Assert.That(Ignored("Assets/Plugin/Thing.cs", "Assets/Plugins"), Is.False);

      // A rule is rooted, not a substring search.
      Assert.That(Ignored("Assets/Vendor/Plugins/Thing.cs", "Assets/Plugins"), Is.False);
    }

    [Test]
    public void ABareNameMatchesThatFolderAtAnyDepth() {
      // Vendored SDKs land wherever the importer put them, so the name travels.
      Assert.That(Ignored("Assets/Firebase/App.cs", "Firebase"), Is.True);
      Assert.That(Ignored("Assets/Plugins/Firebase/App.cs", "Firebase"), Is.True);
      Assert.That(Ignored("Assets/A/B/C/Firebase/App.cs", "Firebase"), Is.True);

      // Only whole segments, never a partial name.
      Assert.That(Ignored("Assets/FirebaseHelpers/App.cs", "Firebase"), Is.False);
      Assert.That(Ignored("Assets/MyFirebase/App.cs", "Firebase"), Is.False);

      // Including the file itself, which is a segment like any other.
      Assert.That(Ignored("Assets/Scripts/Firebase", "Firebase"), Is.True);
    }

    [Test]
    public void AnExceptionWinsWhereverItAppears() {
      var rules = new[] { "Assets/Plugins", "!Assets/Plugins/Android" };
      Assert.That(Ignored("Assets/Plugins/Feel/Demo.cs", rules), Is.True);
      Assert.That(Ignored("Assets/Plugins/Android/AndroidManifest.xml", rules), Is.False);

      // Order must not matter, so nobody has to reason about it.
      var reversed = new[] { "!Assets/Plugins/Android", "Assets/Plugins" };
      Assert.That(Ignored("Assets/Plugins/Android/AndroidManifest.xml", reversed), Is.False);
    }

    [Test]
    public void ComparisonIsOrdinalButCaseInsensitive() {
      // Ordinal per HANDOFF 6.3 -- culture-sensitive comparison diverges in tr-TR, and
      // these are paths. Case-insensitive because the rules are typed by hand.
      Assert.That(Ignored("Assets/plugins/Thing.cs", "Assets/Plugins"), Is.True);
      Assert.That(Ignored("Assets/FIREBASE/App.cs", "firebase"), Is.True);
    }

    [Test]
    public void NothingIsIgnoredWithoutRules() {
      Assert.That(Ignored("Assets/Scripts/Player.cs"), Is.False);
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Scripts/Player.cs", null), Is.False);
      Assert.That(Ignored("Assets/Scripts/Player.cs", "", "   "), Is.False);
      Assert.That(Ignored(null, "Assets/Plugins"), Is.False);
    }

    [Test]
    public void TheShippedListHidesVendoredCodeAndKeepsTheProjectsOwn() {
      var rules = HasteIgnoreRules.Builtin;

      // Third-party, by convention.
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Plugins/Demigiant/DOTween/DOTween.cs", rules), Is.True);
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/ExternalDependencyManager/Editor/x.dll", rules), Is.True);
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Firebase/Editor/x.dll", rules), Is.True);
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/GoogleMobileAds/Api/x.cs", rules), Is.True);

      // Build inputs under Plugins stay findable. This is why exceptions exist.
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Plugins/Android/AndroidManifest.xml", rules), Is.False);
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Plugins/iOS/Native.mm", rules), Is.False);

      // The reason every shipped rule is path-rooted rather than a bare name. Measured
      // against a real project: a bare "Firebase" rule also hid this, the project's OWN
      // wrapper code, named after the SDK it wraps. A shipped list cannot see the
      // project's layout, so it must not guess at any depth.
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Scripts/Firebase/FirebaseService.cs", rules), Is.False);
      Assert.That(HasteIgnoreRules.IsIgnored("Assets/Scripts/Ads/GoogleMobileAds/Wrapper.cs", rules), Is.False);

      // A project's own work is never touched.
      foreach (var mine in new[] {
        "Assets/Scripts/Puzzle/PuzzleController.cs",
        "Assets/Sprites/Puzzle/Popup.png",
        "Assets/Prefabs/Rooms/Study.prefab",
        "Assets/Scenes/Dev/Sandbox.unity",
        "Assets/Settings/Config.asset",
      }) {
        Assert.That(HasteIgnoreRules.IsIgnored(mine, rules), Is.False, mine + " was ignored");
      }
    }

    [Test]
    public void AnIgnoredFolderIsOnlyWalkedWhenSomethingBelowIsExcepted() {
      // The crawler prunes ignored folders, so an exception under one is unreachable
      // unless the walk is allowed to continue into it.
      var rules = HasteIgnoreRules.Builtin.ToList();

      Assert.That(HasteIgnore.HasExceptionUnder("Assets/Plugins", rules), Is.True);
      Assert.That(HasteIgnore.HasExceptionUnder("Assets/Firebase", rules), Is.False);

      // A bare-name exception could be anywhere, so it always forces the walk.
      Assert.That(HasteIgnore.HasExceptionUnder("Assets/Anything", new[] { "!Keepme" }), Is.True);
    }

    [Test]
    public void ParseReadsTheCommaSeparatedUserList() {
      Assert.That(HasteIgnoreRules.Parse("Assets/A, Assets/B ,,  Assets/C "),
        Is.EqualTo(new[] { "Assets/A", "Assets/B", "Assets/C" }));
      Assert.That(HasteIgnoreRules.Parse(""), Is.Empty);
      Assert.That(HasteIgnoreRules.Parse(null), Is.Empty);
    }
  }
}
