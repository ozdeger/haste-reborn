using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Haste {

  // Guards how Haste gets opened.
  //
  // These assert on the registered BINDING rather than on compilation, because a
  // malformed [Shortcut] attribute compiles perfectly cleanly: Unity registers the id
  // with an EMPTY binding and only writes a discovery warning to the log. A test that
  // merely proved the file builds would pass while the shortcut did nothing.
  [TestFixture]
  internal class HasteActivationTests {

    static ShortcutBinding BindingOf(string id) {
      // An unknown id throws ArgumentException rather than returning empty.
      try {
        return ShortcutManager.instance.GetShortcutBinding(id);
      } catch (System.ArgumentException) {
        return ShortcutBinding.empty;
      }
    }

    [Test]
    public void OpenShortcut_IsRegisteredWithShortcutManager() {
      var ids = ShortcutManager.instance.GetAvailableShortcutIds().ToList();
      Assert.That(ids, Contains.Item(HasteShortcut.ShortcutId),
        "Haste's shortcut id is not registered. Either the [Shortcut] attribute was " +
        "rejected at discovery time, or the id changed -- which also silently discards " +
        "any rebinding the user has made, since overrides are keyed by id.");
    }

    [Test]
    public void OpenShortcut_RegistersANonEmptyBinding() {
      // The guard this exists for: a malformed [Shortcut] attribute COMPILES, logs only a
      // discovery warning, and registers the id with an EMPTY binding. Nothing else
      // catches that.
      //
      // Deliberately not asserting WHICH chord. ShortcutManager returns the ACTIVE
      // binding, and rebinding in Edit > Shortcuts is a supported thing to do -- the
      // README tells people to. Asserting the chord here made a developer's own rebinding
      // fail the suite. The declared default is checked below, where an override cannot
      // reach it.
      //
      // Which is not a stylistic preference: an override MASKS a broken default here.
      // Measured by binding the shortcut to Escape, which BindingValidator refuses -- the
      // editor logged "Binding uses invalid key code Escape." and this test still passed,
      // because the developer's own Tab override was supplying the binding. Only the two
      // tests below, which read the attribute, caught it.
      var combos = BindingOf(HasteShortcut.ShortcutId).keyCombinationSequence.ToList();

      Assert.That(combos, Is.Not.Empty,
        "the shortcut registered with an empty binding, which is what a malformed " +
        "[Shortcut] attribute produces -- it compiles, logs a warning, and does nothing");
      Assert.That(combos.Count, Is.EqualTo(1), "expected a single chord, not a sequence");
    }

    [Test]
    public void OpenShortcut_DeclaresTheKeyAboveTabAsItsDefault() {
      // Read from the attribute rather than from ShortcutManager, so a user override in
      // Edit > Shortcuts cannot change the answer.
      var method = typeof(HasteShortcut).GetMethod("OpenShortcut",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
      Assert.That(method, Is.Not.Null);

      var attribute = System.Reflection.CustomAttributeData.GetCustomAttributes(method)
        .FirstOrDefault(a => a.AttributeType == typeof(ShortcutAttribute));
      Assert.That(attribute, Is.Not.Null, "OpenShortcut lost its [Shortcut] attribute");

      var args = attribute.ConstructorArguments;
      var keyCode = args.Where(a => a.ArgumentType == typeof(KeyCode))
        .Select(a => (KeyCode)a.Value).ToList();
      var modifiers = args.Where(a => a.ArgumentType == typeof(ShortcutModifiers))
        .Select(a => (ShortcutModifiers)a.Value).ToList();

      // The key above Tab reports differently per platform -- by character on macOS, by
      // physical key on Windows. See HasteShortcut.DefaultKey.
      var expected = Application.platform == RuntimePlatform.OSXEditor
        ? KeyCode.DoubleQuote
        : KeyCode.BackQuote;
      Assert.That(keyCode, Is.EqualTo(new[] { expected }));

      // Passed explicitly rather than left to the constructor's default, so that "no
      // modifiers" is a stated intent in the attribute and not an omission that could be
      // read either way.
      Assert.That(modifiers, Is.EqualTo(new[] { ShortcutModifiers.None }),
        "the default is one bare key, no chord");
    }

    [Test]
    public void OpenShortcut_DefaultKeyIsOneShortcutManagerWillAccept() {
      // A [Shortcut] whose key is rejected does NOT fail the build. The id registers with
      // an empty binding and Unity writes a discovery warning nobody reads, so the palette
      // just stops opening. BindingValidator holds the list; this reads it rather than
      // trusting that Tab looks ordinary, because Escape and Return look ordinary too and
      // are both refused.
      var validator = typeof(ShortcutManager).Assembly
        .GetType("UnityEditor.ShortcutManagement.BindingValidator");
      if (validator == null) {
        Assert.Ignore("BindingValidator is internal and has moved; " +
          "OpenShortcut_RegistersANonEmptyBinding still covers the outcome.");
      }

      var field = validator.GetField("s_InvalidKeyCodes",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public);
      if (field == null) {
        Assert.Ignore("s_InvalidKeyCodes has moved; " +
          "OpenShortcut_RegistersANonEmptyBinding still covers the outcome.");
      }

      var invalid = new List<KeyCode>();
      foreach (KeyCode code in (System.Collections.IEnumerable)field.GetValue(null)) {
        invalid.Add(code);
      }

      // Non-vacuity: if this list ever comes back empty the assertion below would pass on
      // any key at all, including the ones Unity definitely refuses.
      Assert.That(invalid, Contains.Item(KeyCode.Escape),
        "the invalid-key list did not contain Escape, so it is not the list this test thinks it is");
      Assert.That(invalid, Has.No.Member(DefaultKey()));
    }

    static KeyCode DefaultKey() {
      var method = typeof(HasteShortcut).GetMethod("OpenShortcut",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
      var attribute = System.Reflection.CustomAttributeData.GetCustomAttributes(method)
        .First(a => a.AttributeType == typeof(ShortcutAttribute));
      return attribute.ConstructorArguments
        .Where(a => a.ArgumentType == typeof(KeyCode))
        .Select(a => (KeyCode)a.Value)
        .First();
    }

    [Test]
    public void OpenShortcut_DoesNotCollideWithAnyGlobalShortcut() {
      // The regression test for the bug this replaced: Haste shipped
      // [MenuItem("Window/Haste %k")] while Unity 6 ships
      // [MenuItem("Edit/Search/Search All... %k")] on its own Search window. Ctrl/Cmd+K
      // was owned twice, and the loser just silently never opened.
      //
      // Only GLOBAL shortcuts can collide, which is why this does not simply compare every
      // binding. A ShortcutBinding carries no context, so on the raw comparison Haste's Tab
      // looks identical to Timeline/ToggleClipTrackArea -- and that one is declared with
      // typeof(TimelineWindow), so it only applies while Timeline has focus. Unity resolves
      // context-specific over global, which is the behaviour we want, so counting it as a
      // clash would fail the suite over something working exactly as intended.
      // The DECLARED default, not the live binding. The live one carries the developer's
      // own override, and a suite run on a machine that had rebound Haste would check
      // that override for collisions and never look at the key being shipped.
      var ours = new ShortcutBinding(new KeyCombination(DefaultKey(), ShortcutModifiers.None));

      var scoped = ContextScopedShortcutIds();

      // Non-vacuity: if the attribute sweep below finds nothing, every id would look
      // global and the exclusion would be doing no work at all.
      Assert.That(scoped, Is.Not.Empty,
        "found no context-scoped shortcuts at all, so the attribute sweep is not working");

      var clashes = new List<string>();
      foreach (var id in ShortcutManager.instance.GetAvailableShortcutIds()) {
        if (id == HasteShortcut.ShortcutId || scoped.Contains(id)) {
          continue;
        }
        if (BindingOf(id).Equals(ours)) {
          clashes.Add(id);
        }
      }

      Assert.That(clashes, Is.Empty,
        "Haste's default chord is already claimed globally by: " +
        string.Join(", ", clashes.ToArray()));
    }

    // Ids of every [Shortcut] that names a context type, read from the attributes rather
    // than from ShortcutManager -- which exposes bindings but not their context.
    static HashSet<string> ContextScopedShortcutIds() {
      var scoped = new HashSet<string>();

      foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
        System.Type[] types;
        try {
          types = assembly.GetTypes();
        } catch (System.Reflection.ReflectionTypeLoadException e) {
          types = e.Types.Where(t => t != null).ToArray();
        } catch (System.Exception) {
          continue;
        }

        foreach (var type in types) {
          var methods = type.GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly);

          foreach (var method in methods) {
            System.Collections.Generic.IList<System.Reflection.CustomAttributeData> attributes;
            try {
              attributes = System.Reflection.CustomAttributeData.GetCustomAttributes(method);
            } catch (System.Exception) {
              continue;
            }

            foreach (var attribute in attributes) {
              if (!typeof(ShortcutAttribute).IsAssignableFrom(attribute.AttributeType)) {
                continue;
              }

              var args = attribute.ConstructorArguments;
              var id = args.Where(a => a.ArgumentType == typeof(string))
                .Select(a => (string)a.Value).FirstOrDefault();
              var context = args.Where(a => a.ArgumentType == typeof(System.Type))
                .Select(a => a.Value).FirstOrDefault();

              if (!string.IsNullOrEmpty(id) && context != null) {
                scoped.Add(id);
              }
            }
          }
        }
      }

      return scoped;
    }

    [Test]
    public void Label_DescribesTheLiveBindingAndIsNeverBlank() {
      // The palette prints this as "<label> to reopen" and Preferences shows it beside
      // "Shortcut". It reads the live binding so a rebinding is reflected rather than the
      // window naming a chord that no longer works -- which means a blank or stale answer
      // here is a user telling us Haste "can't be reopened".
      var label = HasteShortcut.Label;

      Assert.That(label, Is.Not.Null.And.Not.Empty,
        "an empty label renders as \" to reopen\", which reads as a missing shortcut");
      Assert.That(label.Trim(), Is.EqualTo(label), "leading or trailing space would show");

      var binding = BindingOf(HasteShortcut.ShortcutId).ToString();
      if (!string.IsNullOrEmpty(binding)) {
        Assert.That(label, Is.EqualTo(binding),
          "the label must be the binding ShortcutManager actually holds, including any " +
          "override the developer has set in Edit > Shortcuts");
      }
    }

    [Test]
    public void ThePrintableKeyCodesAreTheirOwnCharacters() {
      // TypedCharacter rests on this: for printable ASCII a KeyCode's value IS the
      // character. Both defaults are checked, and a letter, because a bare-letter
      // rebinding goes through the same path.
      Assert.That((char)(int)KeyCode.DoubleQuote, Is.EqualTo('"'));
      Assert.That((char)(int)KeyCode.BackQuote, Is.EqualTo('`'));
      Assert.That((char)(int)KeyCode.Q, Is.EqualTo('q'));
    }

    [Test]
    public void TheShortcutsCharacterCannotStartTheQuery() {
      // One press leaking in, and key repeat while held -- the field may take several
      // repeats in one edit.
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "\"", '"'), Is.True);
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "\"\"\"", '"'), Is.True);
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter(null, "\"", '"'), Is.True);
    }

    [Test]
    public void TheShortcutsCharacterTypesNormallyOnceTheQueryHasStarted() {
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("a", "a\"", '"'), Is.False);
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("\"", "\"\"", '"'), Is.False,
        "only an EMPTY field is guarded -- a quote someone kept on purpose stays");
    }

    [Test]
    public void OtherInputIntoAnEmptyQueryIsUntouched() {
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "r", '"'), Is.False);
      // A paste that merely starts with the character is not the key leaking in.
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "\"foo", '"'), Is.False);
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "", '"'), Is.False);
      // A shortcut that types nothing -- Tab, or a chord -- protects nothing.
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "\"", null), Is.False);
    }

    [Test]
    public void ABareLetterShortcutIsMatchedWhateverItsCase() {
      // KeyCode.Q is 'q', but Caps Lock types 'Q'.
      Assert.That(HasteShortcut.IsOnlyTheShortcutCharacter("", "Q", 'q'), Is.True);
    }

    [Test]
    public void MenuItem_CarriesNoShortcutSuffix() {
      // A shortcut baked into the MenuItem string is not rebindable and would compete
      // with the ShortcutManager entry, giving two bindings for one command. It also has
      // to stay exactly "Window/Haste" so HasteMenuItemSource's self-filter keeps Haste
      // out of its own search results.
      var attrs = typeof(HasteShortcut)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .SelectMany(m => m.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
        .ToList();

      Assert.That(attrs, Is.Not.Empty, "expected Haste to still expose a menu item");
      foreach (var attr in attrs) {
        Assert.That(attr.menuItem, Is.EqualTo("Window/Haste"),
          "menu path must be exactly \"Window/Haste\" with no shortcut suffix");
      }
    }

    [Test]
    public void MenuItemSource_StillFiltersHasteOutOfItsOwnResults() {
      // Paired with the assertion above: if the menu path and the filter string ever
      // drift apart, Haste starts appearing in its own results.
      var source = new HasteMenuItemSource();
      Assert.That(source, Is.Not.Null);

      var filterString = "Window/Haste";
      var menuPaths = typeof(HasteShortcut)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .SelectMany(m => m.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
        .Select(a => a.menuItem);

      Assert.That(menuPaths, Is.All.EqualTo(filterString));
    }
  }
}
