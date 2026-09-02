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

      var sheet = HasteResources.Load<UnityEngine.UIElements.StyleSheet>("UI/HasteSpotlight.uss");
      Assert.That(sheet, Is.Not.Null, "the palette stylesheet failed to load");
    }

    [Test]
    public void PackagedResources_MissingAssetReturnsNullInsteadOfThrowing() {
      // A damaged install should degrade to default styling, not take the window down.
      // Load logs a warning, so tell the test framework to expect it.
      UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
        @"\[Haste\] Missing packaged resource"));
      Assert.That(HasteResources.Load<UnityEngine.UIElements.StyleSheet>("UI/NoSuchSheet.uss"), Is.Null);
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
      // glyph atlas. Styles with richText set did not set their own font, so it fell back
      // to the built-in Inspector skin's font -- and reading it succeeds while USING it
      // throws UnassignedReferenceException from the native call. That fired on every
      // scheduler tick from Haste.Update as soon as the package loaded. Both the pre-cache
      // and HasteStyles itself are gone now; this keeps the Unity fact under a test.
      //
      // If a future Unity assigns this again, this test fails and tells us the ground
      // shifted. It does not mean the pre-cache should come back:
      // Font.RequestCharactersInTexture has no callers left in Unity's own editor
      // assemblies, because IMGUI text now routes through IMGUITextHandle into TextCore.
      var skin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);
      Assert.That(skin, Is.Not.Null);
      Assert.That(skin.font == null, Is.True,
        "EditorSkin.Inspector now has a font assigned; see HANDOFF.md 3.3, which records " +
        "this as measured behaviour.");
    }

    [Test]
    public void FontPrecacheStaysDeleted() {
      // Guard against reintroducing the pre-cache, which throws on Unity 6 and warms
      // nothing -- Font.RequestCharactersInTexture has no callers left in Unity's own
      // editor assemblies, so a null-guarded version would just be a slower no-op.
      //
      // This used to scan HasteStyles, which no longer exists. Scanning the whole editor
      // assembly is the stronger guard anyway: it does not matter which type it comes
      // back in.
      var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                  System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;

      foreach (var type in typeof(HasteResources).Assembly.GetTypes()) {
        foreach (var member in type.GetMembers(flags)) {
          Assert.That(member.Name, Does.Not.Contain("PreCache"),
            type.Name + "." + member.Name + " looks like the deleted font pre-cache. See " +
            "HANDOFF.md 3.3 before adding it back.");
        }
      }
    }

    [Test]
    public void WindowBalancesItsReloadLockThroughOnDestroy() {
      // A leaked EditorApplication reload lock is silent and severe -- script changes stop
      // compiling until the editor restarts. It used to be released only from a
      // `new`-shadowed Close(), which every destruction path Unity drives itself skipped,
      // while Open() took the lock even when a window was already open.
      var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;

      Assert.That(typeof(HasteSpotlightWindow).GetMethod("Close", flags), Is.Null,
        "HasteSpotlightWindow declares its own Close() again. Shadowing EditorWindow.Close is how " +
        "the reload lock leaked -- release it from OnDestroy, which Unity always calls.");

      Assert.That(typeof(HasteSpotlightWindow).GetMethod("OnDestroy", flags), Is.Not.Null,
        "HasteSpotlightWindow.OnDestroy is where the reload lock is released.");

      Assert.That(typeof(HasteSpotlightWindow).GetField("holdsReloadLock", flags), Is.Not.Null,
        "the lock is balanced by state, not by pairing call sites.");
    }

    [Test]
    public void PaletteStylesheetShipsAndDeclaresWhatTheWindowUses() {
      // The palette is styled entirely from USS, so a missing or renamed selector is a
      // silently unstyled window rather than an error. Neither the sheet loading nor the
      // class names can be checked by opening the window headlessly -- UI Toolkit needs an
      // interactive editor -- so they are checked as packaging instead.
      var sheet = HasteResources.Load<UnityEngine.UIElements.StyleSheet>("UI/HasteSpotlight.uss");
      Assert.That(sheet, Is.Not.Null, "the palette stylesheet is not in the package");

      var path = HasteResources.Root + "UI/HasteSpotlight.uss";
      var text = File.ReadAllText(path);

      foreach (var required in new[] {
        ".haste-root", ".haste-header", ".haste-badge", ".haste-scope", ".haste-query",
        ".haste-hints", ".haste-hint", ".haste-divider", ".haste-body", ".haste-list",
        ".haste-row", ".haste-row--highlighted", ".haste-tag", ".haste-name",
        ".haste-name--prefab", ".haste-name--broken", ".haste-name--disabled",
        ".haste-spacer", ".haste-path", ".haste-dot", ".haste-message",
        ".haste-message-box", ".haste-message-title", ".haste-message-hint",
        ".haste-footer", ".haste-footer-icon", ".haste-footer-icon--indexing",
        ".haste-status", ".haste-action-label", ".haste-key", ".haste-count",
      }) {
        Assert.That(text, Does.Contain(required),
          "HasteSpotlightWindow adds \"" + required + "\" but the stylesheet does not declare it");
      }
    }

    [Test]
    public void PaletteHasNoWindowOpenByDefault() {
      Assert.That(HasteSpotlightWindow.IsOpen, Is.False);
      Assert.That(HasteSpotlightWindow.WindowWidth, Is.EqualTo(708), "the design's width");
    }

    [Test]
    public void PreferencesAreRegisteredAsASettingsProvider() {
      // [PreferenceItem] is deprecated. The replacement lands in the same place in the UI
      // and is additionally searchable, which [PreferenceItem] pages never were.
      var provider = HastePreferences.CreateSettingsProvider();
      Assert.That(provider, Is.Not.Null);
      Assert.That(provider.settingsPath, Is.EqualTo(HastePreferences.SettingsPath));
      Assert.That(provider.scope, Is.EqualTo(SettingsScope.User));
      Assert.That(provider.keywords, Is.Not.Empty, "the page would not be findable by search");

      // And the deprecated attribute is not hanging around on some other method.
      foreach (var method in typeof(HastePreferences).GetMethods(
                 System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                 System.Reflection.BindingFlags.Static)) {
        foreach (var attr in method.GetCustomAttributes(false)) {
          Assert.That(attr.GetType().Name, Is.Not.EqualTo("PreferenceItemAttribute"),
            "HastePreferences." + method.Name + " still uses the deprecated [PreferenceItem].");
        }
      }
    }

    [Test]
    public void EditorStateIsEventDrivenRatherThanPolled() {
      // Haste.Update runs on every editor tick. It used to compare cached copies of
      // Selection.activeInstanceID and EditorApplication.currentScene against live values
      // there -- two obsolete calls per frame, to raise events that the editor already
      // offers as events. Both are now subscriptions.
      var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                  System.Reflection.BindingFlags.DeclaredOnly;

      Assert.That(typeof(Haste).GetField("activeInstanceID", flags), Is.Null,
        "Haste is polling the selection again. Selection.selectionChanged is the event, " +
        "and Selection.activeInstanceID's own suggested replacement does not exist in 6000.0.");
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
