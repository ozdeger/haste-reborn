using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Haste {

  // The project's shared ignore list, committed alongside the code.
  //
  // Lives in ProjectSettings rather than UserSettings on purpose: "we do not search
  // vendored code" is a decision a team makes once, not something each person rediscovers.
  // Personal additions stay in EditorPrefs; the two are unioned with the built-in list.
  //
  // ScriptableSingleton does NOT auto-save -- its hideFlags include DontSaveInEditor and
  // CreateAndLoad re-reads from disk -- so every mutation has to be followed by Save.
  [FilePath("ProjectSettings/HasteIgnorePaths.asset", FilePathAttribute.Location.ProjectFolder)]
  public class HasteIgnorePaths : ScriptableSingleton<HasteIgnorePaths> {

    [SerializeField]
    List<string> paths = new List<string>();

    public List<string> Paths {
      get { return paths; }
    }

    public void Commit() {
      // true: write text, so the file diffs sensibly in review.
      Save(true);
      Haste.Rebuild();
    }
  }
}
