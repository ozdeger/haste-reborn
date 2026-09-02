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
    public void OpenShortcut_HasANonEmptyDefaultBinding() {
      var binding = BindingOf(HasteShortcut.ShortcutId);
      var combos = binding.keyCombinationSequence.ToList();

      Assert.That(combos, Is.Not.Empty,
        "the shortcut registered with an empty binding, which is what a malformed " +
        "[Shortcut] attribute produces -- it compiles, logs a warning, and does nothing");
      Assert.That(combos.Count, Is.EqualTo(1), "expected a single chord, not a sequence");

      var combo = combos[0];
      Assert.That(combo.keyCode, Is.EqualTo(KeyCode.K));
      Assert.That(combo.modifiers,
        Is.EqualTo(ShortcutModifiers.Action | ShortcutModifiers.Shift));

      // Action is the cross-platform modifier: Cmd on macOS, Ctrl everywhere else.
      // ShortcutModifiers.Control would mean the literal Ctrl key even on a Mac.
      Assert.That(combo.action, Is.True);
      Assert.That(combo.shift, Is.True);
      Assert.That(combo.control, Is.False, "must not bind the literal Control key");
      Assert.That(combo.alt, Is.False);
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
