using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Haste {

  // Guards the palette stylesheet against a class of mistake neither the compiler nor the
  // importer treats as an error.
  //
  // USS does not reject an unsupported value. It logs an import WARNING and keeps the
  // declaration with ZERO values; ComputedStyle.ApplyGlobalKeyword then indexes past the
  // end of that empty list on the first repaint, so the palette throws
  // ArgumentOutOfRangeException the moment it opens.
  //
  // That is exactly what "transition-timing-function: cubic-bezier(.4, 0, .2, 1)" did --
  // USS takes named easings only, and the design's timing function was copied verbatim.
  // Nothing else caught it: the compile was clean, the sheet still loaded, and every
  // selector was still declared.
  [TestFixture]
  internal class HasteStylesheetTests {

    const BindingFlags Flags =
      BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static StyleSheet Sheet() {
      var sheet = HasteResources.Load<StyleSheet>("UI/HasteSpotlight.uss");
      Assert.That(sheet, Is.Not.Null, "the palette stylesheet is not in the package");
      return sheet;
    }

    [Test]
    public void ImportsWithoutWarningsOrErrors() {
      var sheet = Sheet();

      foreach (var name in new[] { "importedWithErrors", "importedWithWarnings" }) {
        var member = typeof(StyleSheet).GetProperty(name, Flags);
        if (member == null) {
          Assert.Ignore("StyleSheet." + name + " is no longer reachable; update this guard.");
        }

        Assert.That((bool)member.GetValue(sheet), Is.False,
          "HasteSpotlight.uss " + name + ". Unity keeps a rejected declaration with no " +
          "values and throws on the first repaint, so this is a crash rather than a " +
          "cosmetic complaint. The editor log names the line.");
      }
    }

    [Test]
    public void EveryDeclarationParsedToAtLeastOneValue() {
      // The same failure seen from the other side, in case a future Unity stops flagging
      // it at import. `properties` is a PROPERTY on StyleRule -- the backing field is
      // m_Properties -- which is worth stating because getting that wrong makes this test
      // silently skip rather than fail.
      var rulesMember = typeof(StyleSheet).GetProperty("rules", Flags);
      if (rulesMember == null) {
        Assert.Ignore("StyleSheet.rules is no longer reachable; update this guard.");
      }

      var rules = rulesMember.GetValue(Sheet()) as System.Array;
      Assert.That(rules, Is.Not.Null.And.Property("Length").GreaterThan(0),
        "the stylesheet parsed to no rules at all");

      var empty = new List<string>();

      foreach (var rule in rules) {
        var propertiesMember = rule.GetType().GetProperty("properties", Flags);
        if (propertiesMember == null) {
          Assert.Ignore("StyleRule.properties is no longer reachable; update this guard.");
        }

        var properties = propertiesMember.GetValue(rule) as System.Array;
        if (properties == null) {
          continue;
        }

        foreach (var property in properties) {
          var type = property.GetType();
          var name = type.GetProperty("name", Flags).GetValue(property) as string;
          var values = type.GetProperty("values", Flags).GetValue(property) as System.Array;

          if (values == null || values.Length == 0) {
            empty.Add(name);
          }
        }
      }

      Assert.That(empty, Is.Empty,
        "these declarations parsed to no values and will throw on the first repaint: " +
        string.Join(", ", empty.ToArray()));
    }
  }
}
