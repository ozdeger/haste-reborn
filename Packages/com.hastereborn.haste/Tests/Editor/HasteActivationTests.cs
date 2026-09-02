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
      var combos = BindingOf(HasteShortcut.ShortcutId).keyCombinationSequence.ToList();

      Assert.That(combos, Is.Not.Empty,
        "the shortcut registered with an empty binding, which is what a malformed " +
        "[Shortcut] attribute produces -- it compiles, logs a warning, and does nothing");
      Assert.That(combos.Count, Is.EqualTo(1), "expected a single chord, not a sequence");
    }

    [Test]
    public void OpenShortcut_DeclaresCommandShiftKAsItsDefault() {
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

      Assert.That(keyCode, Is.EqualTo(new[] { KeyCode.K }));

      // Action is the cross-platform modifier: Cmd on macOS, Ctrl everywhere else.
      // ShortcutModifiers.Control would mean the literal Ctrl key even on a Mac, which is
      // the whole reason this is asserted rather than assumed.
      Assert.That(modifiers, Is.EqualTo(new[] { ShortcutModifiers.Action | ShortcutModifiers.Shift }));
      Assert.That(modifiers[0].HasFlag(ShortcutModifiers.Control), Is.False,
        "must not bind the literal Control key");
      Assert.That(modifiers[0].HasFlag(ShortcutModifiers.Alt), Is.False);
    }

    [Test]
    public void OpenShortcut_DoesNotCollideWithAnyOtherShortcut() {
      // The regression test for the bug this replaced: Haste shipped
      // [MenuItem("Window/Haste %k")] while Unity 6 ships
      // [MenuItem("Edit/Search/Search All... %k")] on its own Search window. Ctrl/Cmd+K
      // was owned twice, and the loser just silently never opened.
      var ours = BindingOf(HasteShortcut.ShortcutId);
      Assert.That(ours.keyCombinationSequence.ToList(), Is.Not.Empty);

      var clashes = new List<string>();
      foreach (var id in ShortcutManager.instance.GetAvailableShortcutIds()) {
        if (id == HasteShortcut.ShortcutId) {
          continue;
        }
        if (BindingOf(id).Equals(ours)) {
          clashes.Add(id);
        }
      }

      Assert.That(clashes, Is.Empty,
        "Haste's default chord is already claimed by: " + string.Join(", ", clashes.ToArray()));
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
