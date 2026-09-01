using System;
using UnityEngine;
using UnityEditor;

namespace Haste {

  // Locates assets that ship inside the Haste package.
  //
  // This used to scan every path in the AssetDatabase looking for a
  // "/Haste/Editor/InternalResources/" substring, because the Unity 5 build shipped a
  // DLL that had no idea where it had been installed. As a UPM package the location is
  // known: package-relative paths work whether Haste is embedded in Packages/ or
  // resolved read-only into Library/PackageCache.
  public static class HasteResources {

    public const string PackageName = "com.hastereborn.haste";

    const string DefaultRoot = "Packages/" + PackageName + "/Editor/InternalResources/";

    static string root;

    // Normally the compile-time constant. The PackageInfo lookup only matters if
    // someone vendors the package under a different folder name.
    public static string Root {
      get {
        if (string.IsNullOrEmpty(root)) {
          root = ResolveRoot();
        }
        return root;
      }
    }

    static string ResolveRoot() {
      if (AssetDatabase.IsValidFolder(DefaultRoot.TrimEnd('/'))) {
        return DefaultRoot;
      }

      var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HasteResources).Assembly);
      if (info != null && !string.IsNullOrEmpty(info.assetPath)) {
        return info.assetPath + "/Editor/InternalResources/";
      }

      Debug.LogWarning("[Haste] Could not locate the Haste package's resources folder. " +
        "Falling back to " + DefaultRoot + "; some styles may be missing.");
      return DefaultRoot;
    }

    // Returns null rather than throwing if the asset is missing, so a damaged install
    // degrades to default styling instead of taking the window down.
    public static T Load<T>(string relativePath) where T : UnityEngine.Object {
      var assetPath = Root + relativePath;
      var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
      if (asset == null) {
        Debug.LogWarning("[Haste] Missing packaged resource: " + assetPath);
        return null;
      }
      asset.hideFlags = HideFlags.HideAndDontSave;
      return asset;
    }
  }
}
