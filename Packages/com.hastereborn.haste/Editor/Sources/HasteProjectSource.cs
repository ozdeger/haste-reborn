using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Haste {

  public class HasteProjectSource : IEnumerable<HasteItem> {

    public const string NAME = "Project";

    static string GetNormalizedAssetPath(string assetPath) {
      var path = Path.Combine("Assets", assetPath.TrimStart(Application.dataPath + Path.DirectorySeparatorChar));
      // Normalize Windows paths
      if (Path.DirectorySeparatorChar != '/') {
        path = path.Replace(Path.DirectorySeparatorChar, '/');
      }
      return path;
    }

    public IEnumerator<HasteItem> GetEnumerator() {
      // Resolved once per crawl rather than per path: the list is small but the crawl is
      // tens of thousands of entries.
      //
      // This used to be `path.IndexOf(ignorePath) == 0` over the raw setting -- culture
      // sensitive, and with no segment boundary, so "Assets/Plugins" would also have
      // swallowed "Assets/PluginsCustom".
      var ignoreRules = HasteIgnore.EffectiveRules();

      Queue<string> directories = new Queue<string>();

      // Start with our top-level directory
      directories.Enqueue(Application.dataPath);

      // Traverse all directories
      while (directories.Count > 0) {
        // Traverse current directory
        string currentPath = directories.Dequeue();

        foreach (string filePath in Directory.GetFiles(currentPath)) {
          if (Path.GetExtension(filePath) == ".meta") {
            continue; // Ignore meta files
          }

          if (Path.GetFileName(filePath).StartsWith(".")) {
            continue; // Ignore hidden files
          }

          string path = GetNormalizedAssetPath(filePath);
          if (HasteIgnoreRules.IsIgnored(path, ignoreRules)) {
            continue;
          }

          yield return new HasteItem(path, 0, NAME);
        }

        foreach (string directoryPath in Directory.GetDirectories(currentPath)) {
          if (Path.GetFileName(directoryPath).StartsWith(".")) {
            continue; // Ignore hidden files
          }

          string path = GetNormalizedAssetPath(directoryPath);
          if (HasteIgnoreRules.IsIgnored(path, ignoreRules)) {
            // Not enqueued: an ignored folder's contents are not walked at all, which is
            // where most of the saving comes from. An exception rule under an ignored
            // folder still needs the walk, so only prune when nothing below is excepted.
            if (!HasteIgnore.HasExceptionUnder(path, ignoreRules)) {
              continue;
            }
          } else {
            yield return new HasteItem(path, 0, NAME);
          }

          directories.Enqueue(directoryPath);
        }
      }
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }
  }
}
