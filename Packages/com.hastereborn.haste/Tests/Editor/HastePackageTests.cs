using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // Guards the packaging contract. These are the claims that break silently rather than
  // loudly: a resource that fails to load leaves the palette unstyled, and a package
  // manifest that fails validation makes the whole package vanish from the Package
  // Manager with only a log line.
  [TestFixture]
  internal class HastePackageTests {

    [Test]
    public void PackageIdentity_IsResolvedByUnity() {
      var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HasteResources).Assembly);
      Assert.That(info, Is.Not.Null,
        "Haste.Editor is not part of a resolved package. If this fails, the package " +
        "manifest was rejected -- check that package.json's \"unity\" field is exactly " +
        "<major>.<minor> (\"6000.0\"), because a full version string makes the package " +
        "fail to load entirely.");
      Assert.That(info.name, Is.EqualTo(HasteResources.PackageName));
      Assert.That(info.assetPath, Is.EqualTo("Packages/" + HasteResources.PackageName));
    }

    [Test]
    public void PackagedResources_LoadByPackageRelativePath() {
      // The old implementation scanned every path in the AssetDatabase looking for a
      // "/Haste/Editor/InternalResources/" substring. Package-relative paths work
      // whether the package is embedded or resolved read-only into Library/PackageCache.
      Assert.That(HasteResources.Root,
        Is.EqualTo("Packages/" + HasteResources.PackageName + "/Editor/InternalResources/"));

      var font = HasteResources.Load<Font>("Fonts/FiraSans-Regular.ttf");
      Assert.That(font, Is.Not.Null, "the bundled query font failed to load");
      Assert.That(font.name, Is.EqualTo("FiraSans-Regular"));
    }

    [Test]
    public void PackagedResources_MissingAssetReturnsNullInsteadOfThrowing() {
      // A damaged install should degrade to default styling, not take the window down.
      // Load logs a warning, so tell the test framework to expect it.
      UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
        @"\[Haste\] Missing packaged resource"));
      Assert.That(HasteResources.Load<Font>("Fonts/NoSuchFont.ttf"), Is.Null);
    }

    [Test]
    public void Manifest_DeclaresTheFieldsUnityValidatesStrictly() {
      var manifestPath = "Packages/" + HasteResources.PackageName + "/package.json";
      var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
      Assert.That(asset, Is.Not.Null, manifestPath + " is not in the AssetDatabase");

      var json = asset.text;
      // "unity" must be exactly major.minor. "6000.0.80f1" throws at resolve time and the
      // package fails to load; the release suffix belongs in "unityRelease".
      StringAssert.Contains("\"unity\": \"6000.0\"", json);
      StringAssert.Contains("\"unityRelease\": \"80f1\"", json);
      // The three URL fields are silently dropped unless absolute http/https.
      StringAssert.Contains("\"documentationUrl\": \"https://", json);
      StringAssert.Contains("\"changelogUrl\": \"https://", json);
      StringAssert.Contains("\"licensesUrl\": \"https://", json);
    }

    [Test]
    public void Package_ShipsItsOwnLicenceAndChangelog() {
      var root = "Packages/" + HasteResources.PackageName + "/";
      foreach (var name in new[] { "package.json", "README.md", "CHANGELOG.md", "LICENSE.md" }) {
        Assert.That(AssetDatabase.LoadAssetAtPath<Object>(root + name), Is.Not.Null,
          name + " is missing from the package");
      }
    }

    [Test]
    public void BuiltinSkinFontIsUnassignedOnUnity6() {
      // Documents the Unity 6 behaviour that broke Haste on import.
      //
      // HasteStyles.PreCacheDynamicFonts() asked every rich-text style's font to warm its
      // glyph atlas. Styles with richText set ("Tip", "Description", ...) do not set their
      // own font, so it fell back to the built-in Inspector skin's font -- and reading it
      // succeeds while USING it throws UnassignedReferenceException from the native call.
      // That fired on every scheduler tick from Haste.Update as soon as the package loaded.
      //
      // If a future Unity assigns this again, this test fails and tells us the ground
      // shifted. It does not mean the pre-cache should come back:
      // Font.RequestCharactersInTexture has no callers left in Unity's own editor
      // assemblies, because IMGUI text now routes through IMGUITextHandle into TextCore.
      var skin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);
      Assert.That(skin, Is.Not.Null);
      Assert.That(skin.font == null, Is.True,
        "EditorSkin.Inspector now has a font assigned; revisit the note in HasteStyles " +
        "where PreCacheDynamicFonts used to be.");
    }

    [Test]
    public void FontPrecacheStaysDeleted() {
      // Guard against reintroducing the pre-cache, which throws on Unity 6 and warms
      // nothing. A null-guarded version would just be a slower no-op.
      var members = typeof(HasteStyles).GetMembers(
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);

      foreach (var member in members) {
        Assert.That(member.Name, Does.Not.Contain("PreCache"),
          "HasteStyles." + member.Name + " looks like the deleted font pre-cache. See the " +
          "note in HasteStyles.cs before adding it back.");
      }
    }

    [Test]
    public void RecencyStore_LivesOutsideThePackage() {
      // Writing into the package's own folder fails once Haste is installed read-only.
      var store = HasteRecommendations.instance;
      Assert.That(store, Is.Not.Null);
      Assert.That(store.Get(), Is.Not.Null);

      // ScriptableSingleton resolves ProjectFolder paths against the process working
      // directory, which is the project root in both the editor and batch mode.
      var expected = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings");
      Assert.That(Directory.Exists(expected) || store.Count == 0, Is.True,
        "expected the recency store to live under UserSettings/");
    }
  }
}
